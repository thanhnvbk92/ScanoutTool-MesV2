using Oracle.ManagedDataAccess.Client;
using System;
using System.Data;

namespace ScanOutTool.Services
{
    public class ScanDataService : IScanDataService
    {
        private const string DbPort = "1521";
        private const string DbName = "HSEVNPDB";
        private const string DbUsername = "INFINITY21_JSMES";
        private const string DbPassword = "INFINITY21_JSMES";
        private readonly IConfigService _configService;

        public ScanDataService(IConfigService configService)
        {
            _configService = configService;
        }

        public (bool IsBlocked, string Reason) IsBlocked(string pid)
        {
            try
            {
                using OracleConnection connection = new(GetConnectionString());
                connection.Open();

                using OracleCommand command = new()
                {
                    Connection = connection,
                    CommandText = "SELECT REASON FROM TBL_BLOCK WHERE PID = :pid AND STATUS = 0",
                    CommandType = CommandType.Text
                };

                command.Parameters.Add("pid", OracleDbType.Varchar2).Value = pid;

                using OracleDataReader reader = command.ExecuteReader();
                if (reader.Read())
                {
                    string reason = reader["REASON"]?.ToString() ?? string.Empty;
                    return (true, reason);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking HMES block status: {ex.Message}");
            }

            return (false, string.Empty);
        }

        private string GetConnectionString()
        {
            string dbHost = string.IsNullOrWhiteSpace(_configService.Config.ServerIP)
                ? "10.7.10.56"
                : _configService.Config.ServerIP;

            return $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={dbHost})(PORT={DbPort}))" +
                   $"(CONNECT_DATA=(SERVER=DEDICATED)(SERVICE_NAME={DbName})));" +
                   $"User Id={DbUsername};Password={DbPassword};";
        }
    }
}
