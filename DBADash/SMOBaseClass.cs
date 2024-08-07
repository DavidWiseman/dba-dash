﻿using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Smo;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DBADash
{
    public class SMOBaseClass
    {
        protected readonly SchemaSnapshotDBOptions options;
        protected readonly ScriptingOptions ScriptingOptions;
        protected string ConnectionString => SourceConnection.ConnectionString;
        protected readonly DBADashConnection SourceConnection;

        public SMOBaseClass(DBADashConnection source, SchemaSnapshotDBOptions options)
        {
            SourceConnection = source;
            options ??= new SchemaSnapshotDBOptions();
            this.options = options;
            ScriptingOptions = options.ScriptOptions();
        }

        public SMOBaseClass(DBADashConnection source)
        {
            SourceConnection = source;
            options = new SchemaSnapshotDBOptions();
            ScriptingOptions = options.ScriptOptions();
        }



        protected string MasterConnectionString
        {
            get
            {
                var builder = new SqlConnectionStringBuilder(ConnectionString)
                {
                    InitialCatalog = "master"
                };
                return builder.ConnectionString;
            }
        }

        public static byte[] ComputeHash(byte[] obj)
        {
            using (var crypt = SHA256.Create())
            {
                return crypt.ComputeHash(obj);
            }
        }


        public static string StringCollectionToString(StringCollection sc)
        {
            StringBuilder sb = new();
            foreach (var s in sc)
            {
                sb.AppendLine(s);
                sb.AppendLine("GO");
            }
            return sb.ToString();
        }

        public static void CopyTo(Stream src, Stream dest)
        {
            byte[] bytes = new byte[4096];

            int cnt;

            while ((cnt = src.Read(bytes, 0, bytes.Length)) != 0)
            {
                dest.Write(bytes, 0, cnt);
            }
        }

        public static byte[] Zip(string str)
        {
            var bytes = Encoding.Unicode.GetBytes(str);

            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(mso, CompressionMode.Compress))
                {
                    //msi.CopyTo(gs);
                    CopyTo(msi, gs);
                }

                return mso.ToArray();
            }
        }

        public static string Unzip(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes))
            using (var mso = new MemoryStream())
            {
                using (var gs = new GZipStream(msi, CompressionMode.Decompress))
                {
                    //gs.CopyTo(mso);
                    CopyTo(gs, mso);
                }

                return Encoding.Unicode.GetString(mso.ToArray());
            }
        }

    }
}
