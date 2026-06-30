using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace WxKeyExtractor
{
    internal sealed class SqlCipherDatabase : IDisposable
    {
        private const int SqliteOk = 0;
        private const int SqliteResultRow = 100;
        private const int SqliteDone = 101;
        private const int SqliteOpenReadOnly = 1;
        private IntPtr _database;

        internal SqlCipherDatabase(string path, string processKeyHex)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("数据库不存在", path);
            }

            byte[] password = ParseHexKey(processKeyHex);
            byte[] salt = new byte[16];
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Read(salt, 0, salt.Length) != salt.Length)
                {
                    throw new InvalidDataException("数据库文件过短: " + path);
                }
            }

            byte[] fileName = Utf8Z(path);
            int result = Native.sqlite3_open_v2(fileName, out _database, SqliteOpenReadOnly, IntPtr.Zero);
            if (result != SqliteOk)
            {
                string message = GetError();
                Dispose();
                throw new InvalidOperationException("无法只读打开数据库: " + message);
            }

            byte[] rawKey = DeriveKey(password, salt, 256000, 32);
            try
            {
                Execute("PRAGMA cipher_compatibility = 4;");
                Execute("PRAGMA cipher_page_size = 4096;");
                Execute("PRAGMA key=\"x'" + ToHex(rawKey) + "'\";");
                Execute("SELECT count(*) FROM sqlite_master;");
            }
            catch
            {
                Dispose();
                throw;
            }
            finally
            {
                Array.Clear(password, 0, password.Length);
                Array.Clear(rawKey, 0, rawKey.Length);
            }
        }

        internal void Query(string sql, Action<SqliteRow> consume)
        {
            IntPtr statement;
            byte[] query = Utf8Z(sql);
            int result = Native.sqlite3_prepare_v2(_database, query, -1, out statement, IntPtr.Zero);
            if (result != SqliteOk)
            {
                throw new InvalidOperationException("SQL 准备失败: " + GetError());
            }

            try
            {
                int count = Native.sqlite3_column_count(statement);
                string[] names = new string[count];
                for (int i = 0; i < count; i++)
                {
                    names[i] = ReadUtf8(Native.sqlite3_column_name(statement, i), -1);
                }

                while ((result = Native.sqlite3_step(statement)) == SqliteResultRow)
                {
                    consume(new SqliteRow(statement, names));
                }
                if (result != SqliteDone)
                {
                    throw new InvalidOperationException("SQL 读取失败: " + GetError());
                }
            }
            finally
            {
                Native.sqlite3_finalize(statement);
            }
        }

        private void Execute(string sql)
        {
            byte[] command = Utf8Z(sql);
            int result = Native.sqlite3_exec(_database, command, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (result != SqliteOk)
            {
                throw new InvalidOperationException("数据库解密或查询失败: " + GetError());
            }
        }

        private string GetError()
        {
            return _database == IntPtr.Zero ? "unknown error" : ReadUtf8(Native.sqlite3_errmsg(_database), -1);
        }

        internal static byte[] ParseHexKey(string value)
        {
            string key = (value ?? string.Empty).Trim();
            if (key.Length != 64)
            {
                throw new ArgumentException("数据库 Key 必须是 64 位十六进制字符串。", "value");
            }
            byte[] bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++)
            {
                int high = HexValue(key[i * 2]);
                int low = HexValue(key[i * 2 + 1]);
                if (high < 0 || low < 0)
                {
                    throw new ArgumentException("数据库 Key 包含非十六进制字符。", "value");
                }
                bytes[i] = (byte)((high << 4) | low);
            }
            return bytes;
        }

        private static int HexValue(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        }

        private static byte[] DeriveKey(byte[] password, byte[] salt, int iterations, int length)
        {
            byte[] blockInput = new byte[salt.Length + 4];
            Buffer.BlockCopy(salt, 0, blockInput, 0, salt.Length);
            blockInput[blockInput.Length - 1] = 1;
            using (HMACSHA512 hmac = new HMACSHA512(password))
            {
                byte[] current = hmac.ComputeHash(blockInput);
                byte[] result = (byte[])current.Clone();
                for (int iteration = 1; iteration < iterations; iteration++)
                {
                    current = hmac.ComputeHash(current);
                    for (int index = 0; index < result.Length; index++)
                    {
                        result[index] ^= current[index];
                    }
                }
                byte[] output = new byte[length];
                Buffer.BlockCopy(result, 0, output, 0, length);
                Array.Clear(current, 0, current.Length);
                Array.Clear(result, 0, result.Length);
                return output;
            }
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2"));
            return builder.ToString();
        }

        private static byte[] Utf8Z(string value)
        {
            return Encoding.UTF8.GetBytes(value + "\0");
        }

        internal static string ReadUtf8(IntPtr pointer, int byteCount)
        {
            if (pointer == IntPtr.Zero) return string.Empty;
            if (byteCount < 0)
            {
                byteCount = 0;
                while (Marshal.ReadByte(pointer, byteCount) != 0) byteCount++;
            }
            if (byteCount == 0) return string.Empty;
            byte[] bytes = new byte[byteCount];
            Marshal.Copy(pointer, bytes, 0, byteCount);
            return Encoding.UTF8.GetString(bytes);
        }

        public void Dispose()
        {
            if (_database != IntPtr.Zero)
            {
                Native.sqlite3_close_v2(_database);
                _database = IntPtr.Zero;
            }
        }

        internal sealed class SqliteRow
        {
            private readonly IntPtr _statement;
            private readonly Dictionary<string, int> _columns;

            internal SqliteRow(IntPtr statement, string[] names)
            {
                _statement = statement;
                _columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < names.Length; i++) _columns[names[i]] = i;
            }

            internal string Text(string name)
            {
                int index = _columns[name];
                IntPtr value = Native.sqlite3_column_text(_statement, index);
                return ReadUtf8(value, Native.sqlite3_column_bytes(_statement, index));
            }

            internal long Int64(string name)
            {
                return Native.sqlite3_column_int64(_statement, _columns[name]);
            }
        }

        private static class Native
        {
            [DllImport("e_sqlcipher.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_open_v2(byte[] filename, out IntPtr database, int flags, IntPtr vfs);
            [DllImport("e_sqlcipher.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_close_v2(IntPtr database);
            [DllImport("e_sqlcipher.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr sqlite3_errmsg(IntPtr database);
            [DllImport("e_sqlcipher.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_exec(IntPtr database, byte[] sql, IntPtr callback, IntPtr argument, IntPtr error);
            [DllImport("e_sqlcipher.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_prepare_v2(IntPtr database, byte[] sql, int bytes, out IntPtr statement, IntPtr tail);
            [DllImport("e_sqlcipher.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_step(IntPtr statement);
            [DllImport("e_sqlcipher.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_finalize(IntPtr statement);
            [DllImport("e_sqlcipher.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_column_count(IntPtr statement);
            [DllImport("e_sqlcipher.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr sqlite3_column_name(IntPtr statement, int index);
            [DllImport("e_sqlcipher.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr sqlite3_column_text(IntPtr statement, int index);
            [DllImport("e_sqlcipher.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern int sqlite3_column_bytes(IntPtr statement, int index);
            [DllImport("e_sqlcipher.dll", CallingConvention = CallingConvention.Cdecl)]
            internal static extern long sqlite3_column_int64(IntPtr statement, int index);
        }
    }
}
