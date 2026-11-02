namespace FRFrame
{
    public readonly struct ObjectPoolKey<T> where T : class
    {
        public readonly string Name;

        public ObjectPoolKey(string name)
        {
            Name = string.IsNullOrEmpty(name) ? typeof(T).FullName : name;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
