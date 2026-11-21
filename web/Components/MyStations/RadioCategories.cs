namespace My
{
    public class RadioCategories
    {
        public Dictionary<string, List<Station>> Categories { get; set; } = new Dictionary<string, List<Station>>();
    }

    public class Station
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}
