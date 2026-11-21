using Helpers;

namespace Browser
{
    public class BrowserStation
    {
        public string Id { get; private set; } = string.Empty;
        public string Name { get; private set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Icon { get; private set; } = string.Empty;
        public List<string> Tags { get; private set; } = new List<string>();
        public string CountryCode { get; private set; } = string.Empty;
        public string Language { get; private set; } = string.Empty;
        public int Votes { get; private set; }
        public string Codec { get; private set; } = string.Empty;
        public int Bitrate { get; private set; }

        public BrowserStation(Dictionary<string, string> stationJson)
        {
            // Missing/null attributes must not throw (YCast uses get_json_attr which returns None).
            Id = Generic.GenerateStationidWithPrefix(RadioBrowser.GetJsonAttr(stationJson, "stationuuid"), RadioBrowser.ID_PREFIX);
            Name = RadioBrowser.GetJsonAttr(stationJson, "name");
            Url = RadioBrowser.GetJsonAttr(stationJson, "url");
            Icon = RadioBrowser.GetJsonAttr(stationJson, "favicon");
            Tags = RadioBrowser.GetJsonAttr(stationJson, "tags").Split(",").ToList();
            CountryCode = RadioBrowser.GetJsonAttr(stationJson, "countrycode");
            Language = RadioBrowser.GetJsonAttr(stationJson, "language");
            Votes = int.TryParse(RadioBrowser.GetJsonAttr(stationJson, "votes"), out var votes) ? votes : 0;
            Codec = RadioBrowser.GetJsonAttr(stationJson, "codec");
            Bitrate = int.TryParse(RadioBrowser.GetJsonAttr(stationJson, "bitrate"), out var bitrate) ? bitrate : 0;
        }
        //class Station :
        //    def __init__(self, station_json):
        //        self.id = generic.generate_stationid_with_prefix(get_json_attr(station_json, 'stationuuid'), ID_PREFIX)
        //        self.name = get_json_attr(station_json, 'name')
        //        self.url = get_json_attr(station_json, 'url')
        //        self.icon = get_json_attr(station_json, 'favicon')
        //        self.tags = get_json_attr(station_json, 'tags').split(',')
        //        self.countrycode = get_json_attr(station_json, 'countrycode')
        //        self.language = get_json_attr(station_json, 'language')
        //        self.votes = get_json_attr(station_json, 'votes')
        //        self.codec = get_json_attr(station_json, 'codec')
        //        self.bitrate = get_json_attr(station_json, 'bitrate')

        public VTuner.Station ToVtuner()
        {
            return new VTuner.Station(Id, Name, Tags != null ? string.Join(", ", Tags) : string.Empty, Url, Icon,
                                      Tags != null && Tags.Count > 0 ? Tags[0] : string.Empty, CountryCode, Codec, Bitrate, string.Empty);
        }
        //    def to_vtuner(self):
        //        return vtuner.Station(self.id, self.name, ', '.join(self.tags), self.url, self.icon,
        //                              self.tags[0], self.countrycode, self.codec, self.bitrate, None)
    }
}
