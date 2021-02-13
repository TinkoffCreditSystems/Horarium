using System;
using System.Reflection;
using Cronos;
using Newtonsoft.Json;

namespace Horarium
{
    internal static class Utils
    {
        public static string ToJson(this object obj, Type type, JsonSerializerSettings jsonSerializerSettings)
        {
           return JsonConvert.SerializeObject(obj, type, jsonSerializerSettings);
        }

        public static object FromJson(this string json, Type type, JsonSerializerSettings jsonSerializerSettings)
        {
            return JsonConvert.DeserializeObject(json, type, jsonSerializerSettings);
        }

        public static string AssemblyQualifiedNameWithoutVersion(this Type type)
        {
            string retValue = type.FullName + ", " + type.GetTypeInfo().Assembly.GetName().Name;
            return retValue;
        }
        
        public static DateTime? ParseAndGetNextOccurrence(string cron)
        {
            var expression = CronExpression.Parse(cron, CronFormat.IncludeSeconds);
            
            return expression.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Local);
        }
    }
}