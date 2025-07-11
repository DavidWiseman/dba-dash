﻿using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.ApplicationInsights;

namespace DBADash
{
    public class DBADashConnection
    {
        private readonly List<int> supportedEngineEditions = new() { 1, 2, 3, 4, 5, 8 }; // Personal, Standard, Enterprise, Express, Azure DB, Azure MI
        private readonly List<int> supportedProductVersions = new() { 9, 10, 11, 12, 13, 14, 15, 16, 17 }; // SQL 2005 to 2025 & Azure

        public enum ConnectionType
        {
            SQL,
            Directory,
            AWSS3,
            Invalid
        }

        private readonly string myString = "g&hAs2&mVOLwE6DqO!I5";
        private bool wasEncryptionPerformed;
        private bool isEncrypted;
        private string encryptedConnectionString = "";
        private string connectionString = "";
        private ConnectionType connectionType;

        public bool WasEncrypted => wasEncryptionPerformed;

        public bool IsEncrypted => isEncrypted;

        public bool IsIntegratedSecurity { private set; get; }

        public string UserName { private set; get; }

        public DBADashConnection(string connectionString)
        {
            SetConnectionString(connectionString);
        }

        public DBADashConnection()
        {
        }

        private void SetConnectionString(string value)
        {
            if (GetConnectionType(value) == ConnectionType.SQL)
            {
                var builder = new SqlConnectionStringBuilder(value)
                {
                    ApplicationName = "DBADash"
                };
                IsIntegratedSecurity = builder.IntegratedSecurity || builder.Authentication == SqlAuthenticationMethod.ActiveDirectoryIntegrated;
                UserName = builder.UserID;
                value = builder.ToString();
            }
            encryptedConnectionString = GetConnectionStringWithEncryptedPassword(value);
            connectionString = GetDecryptedConnectionString(value);
            connectionType = GetConnectionType(value);
            EncryptText.GetHash(connectionString);
        }

        [JsonIgnore]
        public string ConnectionString
        {
            get => connectionString;
            set => SetConnectionString(value);
        }

        [JsonIgnore]
        public string MasterConnectionString
        {
            get
            {
                var builder = new SqlConnectionStringBuilder(ConnectionString)
                {
                    InitialCatalog = "master"
                };
                return builder.ToString();
            }
        }

        public DBADashConnection MasterConnection() => new(MasterConnectionString);

        public string EncryptedConnectionString
        {
            get => encryptedConnectionString;
            set => SetConnectionString(value);
        }

        private ConnectionInfo _connectionInfo;

        [JsonIgnore]
        public ConnectionInfo ConnectionInfo
        {
            get
            {
                if (_connectionInfo == null && connectionType == ConnectionType.SQL)
                {
                    _connectionInfo = ConnectionInfo.GetConnectionInfo(connectionString);
                }
                return _connectionInfo;
            }
        }

        public void Validate()
        {
            if (connectionType == ConnectionType.Directory)
            {
                if (Directory.Exists(connectionString) == false)
                {
                    throw new Exception("Directory does not exist");
                }
            }
            else if (connectionType == ConnectionType.SQL)
            {
                ValidateSQLConnection(); // Open a connection to the DB
            }
        }

        public bool IsXESupported()
        {
            return connectionType == ConnectionType.SQL && ConnectionInfo.IsXESupported;
        }

        public static bool IsXESupported(string productVersion)
        {
            return ConnectionInfo.GetXESupported(productVersion);
        }

        public bool IsAzureDB()
        {
            return ConnectionInfo.IsAzureDB;
        }

        private void ValidateSQLConnection()
        {
            if (!supportedProductVersions.Contains(ConnectionInfo.MajorVersion))
            {
                throw new Exception(
                    $"SQL Server Version {ConnectionInfo.MajorVersion} isn't supported by DBA Dash.  For testing purposes, it's possible to skip this validation check.");
            }
            if (!supportedEngineEditions.Contains(ConnectionInfo.EngineEditionValue))
            {
                throw new Exception(
                    $"SQL Server Engine Edition {ConnectionInfo.EngineEditionValue} isn't supported by DBA Dash.  For testing purposes, it's possible to skip this validation check.");
            }
        }

        public ConnectionType Type => connectionType;

        public string InitialCatalog()
        {
            if (connectionType == ConnectionType.SQL)
            {
                SqlConnectionStringBuilder builder = new(connectionString);
                return builder.InitialCatalog;
            }
            else
            {
                return "";
            }
        }

        public ApplicationIntent? ApplicationIntent()
        {
            if (connectionType == ConnectionType.SQL)
            {
                SqlConnectionStringBuilder builder = new(connectionString);
                return builder.ApplicationIntent;
            }
            else
            {
                return null;
            }
        }

        public string DataSource()
        {
            if (connectionType == ConnectionType.SQL)
            {
                SqlConnectionStringBuilder builder = new(connectionString);
                return builder.DataSource;
            }
            else
            {
                return "";
            }
        }

        private static ConnectionType GetConnectionType(string connectionString)
        {
            if (connectionString == null || connectionString.Length < 3)
            {
                return ConnectionType.Invalid;
            }
            else if (connectionString.StartsWith("s3://") || connectionString.StartsWith("https://"))
            {
                return ConnectionType.AWSS3;
            }
            else if (connectionString.StartsWith("\\\\") || connectionString.StartsWith("//") || connectionString.Substring(1, 2) == ":\\")
            {
                return ConnectionType.Directory;
            }
            else
            {
                try
                {
                    SqlConnectionStringBuilder builder = new(connectionString);
                    return ConnectionType.SQL;
                }
                catch
                {
                    return ConnectionType.Invalid;
                }
            }
        }

        private string GetConnectionStringWithEncryptedPassword(string _connectionString)
        {
            if (GetConnectionType(_connectionString) == ConnectionType.SQL)
            {
                SqlConnectionStringBuilder builder = new(_connectionString);

                if (builder.Password.StartsWith("¬=!"))
                {
                    isEncrypted = true;
                    return _connectionString;
                }
                else if (builder.Password.Length > 0)
                {
                    builder.Password = "¬=!" + builder.Password.EncryptString(myString);
                    wasEncryptionPerformed = true;
                    isEncrypted = true;
                    return builder.ConnectionString;
                }
                else
                {
                    return _connectionString;
                }
            }
            else
            {
                return _connectionString;
            }
        }

        private string GetDecryptedConnectionString(string _connectionString)
        {
            if (GetConnectionType(_connectionString) == ConnectionType.SQL)
            {
                SqlConnectionStringBuilder builder = new(_connectionString);
                if (builder.ApplicationName == ".Net SqlClient Data Provider")
                {
                    builder.ApplicationName = "DBADash";
                }
                if (builder.Password.StartsWith("¬=!"))
                {
                    builder.Password = builder.Password[3..].DecryptString(myString);
                }
                return builder.ConnectionString;
            }
            else
            {
                return _connectionString;
            }
        }

        [JsonIgnore]
        public string Hash => EncryptText.GetHash(ConnectionString);

        public string ConnectionForFileName
        {
            get
            {
                if (Type == ConnectionType.SQL)
                {
                    SqlConnectionStringBuilder builder = new(connectionString);
                    string connection = builder.DataSource + (builder.InitialCatalog == "" ? "" : "_" + builder.InitialCatalog);
                    string regexSearch = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
                    Regex r = new($"[{Regex.Escape(regexSearch)}]");
                    return r.Replace(connection, "");
                }
                else
                {
                    throw new Exception("Invalid connection type for filename generation");
                }
            }
        }

        public string ConnectionForPrint
        {
            get
            {
                if (Type == ConnectionType.SQL)
                {
                    SqlConnectionStringBuilder builder = new(connectionString);
                    return builder.DataSource + (builder.InitialCatalog == "" ? "" : "|" + builder.InitialCatalog);
                }
                else
                {
                    return connectionString;
                }
            }
        }
    }
}