namespace ViWatcher.Shared.Models
{
    using ViWatcher.Plugins;
    public class ElementField
    {
        public int Order { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public FormInputType InputType { get; set; }
    }

}