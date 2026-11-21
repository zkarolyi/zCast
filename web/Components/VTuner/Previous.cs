using System.Xml;

namespace VTuner
{
    public class Previous : PageItems
    {
        public string Url { get; set; }

        public Previous(string url)
        {
            this.Url = url;
        }

        public override XmlElement ToXml(XmlDocument xml)
        {
            var item = xml.CreateElement("Item");

            var itemType = xml.CreateElement("ItemType");
            itemType.InnerText = "Previous";
            item.AppendChild(itemType);

            var urlPrevious = xml.CreateElement("UrlPrevious");
            urlPrevious.InnerText = Page.AddBogusParameter(Url);
            item.AppendChild(urlPrevious);

            var urlPreviousBackUp = xml.CreateElement("UrlPreviousBackUp");
            urlPreviousBackUp.InnerText = Page.AddBogusParameter(Url);
            item.AppendChild(urlPreviousBackUp);

            return item;
        }
    }
}
