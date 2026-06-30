using System.IO;
using System.Text;
using System.Threading;

namespace WxKeyExtractor
{
    internal static class WeChatDataExporter
    {
        internal static int ExportContacts(string storage, string key, string output, CancellationToken token)
        {
            string databasePath = Path.Combine(storage, "contact", "contact.db");
            int count = 0;
            using (StreamWriter writer = new StreamWriter(output, false, new UTF8Encoding(true)))
            using (SqlCipherDatabase database = new SqlCipherDatabase(databasePath, key))
            {
                WriteCsv(writer, "显示名称", "备注", "昵称", "微信号", "别名", "描述");
                database.Query("SELECT username,alias,remark,nick_name,description FROM contact WHERE delete_flag=0 AND local_type=1 AND verify_flag=0 AND username NOT LIKE '%@chatroom' AND username NOT LIKE 'gh_%' ORDER BY COALESCE(NULLIF(remark,''),nick_name,username)", delegate(SqlCipherDatabase.SqliteRow row)
                {
                    token.ThrowIfCancellationRequested();
                    string display = row.Text("remark");
                    if (string.IsNullOrWhiteSpace(display)) display = row.Text("nick_name");
                    WriteCsv(writer, display, row.Text("remark"), row.Text("nick_name"), row.Text("username"), row.Text("alias"), row.Text("description"));
                    count++;
                });
            }
            return count;
        }

        private static void WriteCsv(StreamWriter writer, params string[] values)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (index > 0) writer.Write(',');
                string value = values[index] ?? string.Empty;
                writer.Write('"');
                writer.Write(value.Replace("\"", "\"\""));
                writer.Write('"');
            }
            writer.WriteLine();
        }
    }
}
