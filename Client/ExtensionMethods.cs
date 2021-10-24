namespace ViWatcher.Client 
{
    using System.Net.Http;
    using System.Text;
    using ViWatcher.Shared;

    public static class ExtensionMethods 
    {
        public static StringContent AsJson(this object o)
        {
            string json = o.ToJson();
            return new StringContent(json, Encoding.UTF8, "application/json");
        }
    }
}