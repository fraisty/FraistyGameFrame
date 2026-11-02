using System;
using System.Text;
using Newtonsoft.Json;

namespace FRFrame
{
    public sealed class NewtonsoftJsonSaveSerializer : ISaveSerializer
    {
        private readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            TypeNameHandling = TypeNameHandling.None,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        public string FileExtension => ".json";

        public byte[] Serialize<T>(T value)
        {
            string json = JsonConvert.SerializeObject(value, _settings);
            return Encoding.UTF8.GetBytes(json);
        }

        public T Deserialize<T>(byte[] bytes)
        {
            string json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject<T>(json, _settings);
        }

        public object Deserialize(byte[] bytes, Type type)
        {
            string json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject(json, type, _settings);
        }
    }
}
