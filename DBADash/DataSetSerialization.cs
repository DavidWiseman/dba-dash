using System;
using System.Data;
using System.IO;

namespace DBADash
{
    public class DataSetSerialization
    {
        public static void SetDateTimeKind(DataSet ds) // Required for binary serialization to prevent dates captured in UTC from being converted to local timezone on deserialization
        {
            foreach (DataTable dt in ds.Tables)
            {
                foreach (DataColumn col in dt.Columns)
                {
                    if (col.DataType == typeof(DateTime))
                    {
                        col.DateTimeMode = DataSetDateTime.Unspecified;
                    }
                }
            }
        }
        
        public static DataSet DeserializeFromXmlFile(string filePath)
        {
            // FileMode.Open (not OpenOrCreate).  A file that was deleted between being listed and read here
            // must throw FileNotFoundException so the caller can skip it.  Creating the file instead leaves a
            // zero byte file behind that can never be imported ("Root element is missing").
            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
            var ds = new DataSet();
            ds.ReadXml(fs);
            return ds;
        }

        public static DataSet DeserializeFromFile(string filePath)
        {
            if (filePath.EndsWith(".xml"))
            {
                return DeserializeFromXmlFile(filePath);
            }
            else
            {
                throw new Exception($"Invalid file extension {filePath} expected: .xml");
            }
        }
    }
}