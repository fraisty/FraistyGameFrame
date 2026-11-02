using System;

namespace FRFrame
{
    public interface ISaveSerializer
    {
        string FileExtension { get; }
        byte[] Serialize<T>(T value);
        T Deserialize<T>(byte[] bytes);
        object Deserialize(byte[] bytes, Type type);
    }
}
