using System.Xml;

namespace VTuner
{
    public class Station : PageItems
    {
        public string Uid { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
        public string Trackurl { get; set; }
        public string Icon { get; set; }
        public string Genre { get; set; }
        public string Location { get; set; }
        public string Mime { get; set; }
        public int Bitrate { get; set; }
        public string Bookmark { get; set; }

        public Station(string uid, string name, string description, string url, string icon, string genre, string location, string mime, int bitrate, string bookmark)
        {
            this.Uid = uid;
            this.Name = name;
            this.Description = description;
            this.Url = Page.StripHttps(url);
            this.Trackurl = string.Empty;
            this.Icon = icon;
            this.Genre = genre;
            this.Location = location;
            this.Mime = mime;
            this.Bitrate = bitrate;
            this.Bookmark = bookmark;
        }

        public void SetTrackUrl(string url)
        {
            this.Trackurl = url;
        }

        public override XmlElement ToXml(XmlDocument xml)
        {
            var item = xml.CreateElement("Item");

            var itemType = xml.CreateElement("ItemType");
            itemType.InnerText = "Station";
            item.AppendChild(itemType);

            var stationId = xml.CreateElement("StationId");
            stationId.InnerText = Uid;
            item.AppendChild(stationId);

            var stationName = xml.CreateElement("StationName");
            stationName.InnerText = Name;
            item.AppendChild(stationName);

            var stationUrl = xml.CreateElement("StationUrl");
            stationUrl.InnerText = string.IsNullOrEmpty(Trackurl) ? Url : Trackurl;
            item.AppendChild(stationUrl);

            var stationDesc = xml.CreateElement("StationDesc");
            stationDesc.InnerText = Description;
            item.AppendChild(stationDesc);

            var logo = xml.CreateElement("Logo");
            logo.InnerText = Icon;
            item.AppendChild(logo);

            var stationFormat = xml.CreateElement("StationFormat");
            stationFormat.InnerText = Genre;
            item.AppendChild(stationFormat);

            var stationLocation = xml.CreateElement("StationLocation");
            stationLocation.InnerText = Location;
            item.AppendChild(stationLocation);

            var stationBandwidth = xml.CreateElement("StationBandWidth");
            stationBandwidth.InnerText = Bitrate.ToString();
            item.AppendChild(stationBandwidth);

            var stationMime = xml.CreateElement("StationMime");
            stationMime.InnerText = Mime;
            item.AppendChild(stationMime);

            var relia = xml.CreateElement("Relia");
            relia.InnerText = "3";
            item.AppendChild(relia);

            var bookmark = xml.CreateElement("Bookmark");
            bookmark.InnerText = this.Bookmark;
            item.AppendChild(bookmark);

            return item;
        }
    }
}
