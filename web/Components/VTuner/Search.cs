using System.Xml;

namespace VTuner
{
    public class Search : PageItems
    {
        public string Caption { get; set; }
        public string Url { get; set; }

        public Search(string caption, string url)
        {
            this.Caption = caption;
            this.Url = url;
        }

        public override XmlElement ToXml(XmlDocument xml)
        {
            var item = xml.CreateElement("Item");

            var itemType = xml.CreateElement("ItemType");
            itemType.InnerText = "Search";
            item.AppendChild(itemType);

            var searchUrl = xml.CreateElement("SearchURL");
            searchUrl.InnerText = Page.AddBogusParameter(Url);
            item.AppendChild(searchUrl);

            var searchUrlBackUp = xml.CreateElement("SearchURLBackUp");
            searchUrlBackUp.InnerText = Page.AddBogusParameter(Url);
            item.AppendChild(searchUrlBackUp);

            var searchCaption = xml.CreateElement("SearchCaption");
            searchCaption.InnerText = Caption;
            item.AppendChild(searchCaption);

            var searchTextbox = xml.CreateElement("SearchTextbox");
            item.AppendChild(searchTextbox);

            var searchButtonGo = xml.CreateElement("SearchButtonGo");
            searchButtonGo.InnerText = "Search";
            item.AppendChild(searchButtonGo);

            var searchButtonCancel = xml.CreateElement("SearchButtonCancel");
            searchButtonCancel.InnerText = "Cancel";
            item.AppendChild(searchButtonCancel);

            return item;
        }
    }

}
