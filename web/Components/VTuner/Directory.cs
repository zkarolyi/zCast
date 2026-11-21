using System.Xml;

namespace VTuner
{
    public class Directory : PageItems
    {
        public string Title { get; set; }
        public string Destination { get; set; }
        public int ItemCount { get; set; }

        public Directory(string title, string destination, int itemCount = -1)
        {
            this.Title = title;
            this.Destination = destination;
            this.ItemCount = itemCount;
        }

        public override XmlElement ToXml(XmlDocument xml)
        {
            var item = xml.CreateElement("Item");

            var itemType = xml.CreateElement("ItemType");
            itemType.InnerText = "Dir";
            item.AppendChild(itemType);

            var title = xml.CreateElement("Title");
            title.InnerText = this.Title;
            item.AppendChild(title);

            var urlDir = xml.CreateElement("UrlDir");
            urlDir.InnerText = Page.AddBogusParameter(Destination);
            item.AppendChild(urlDir);

            var urlDirBackUp = xml.CreateElement("UrlDirBackUp");
            urlDirBackUp.InnerText = Page.AddBogusParameter(Destination);
            item.AppendChild(urlDirBackUp);

            var dirCount = xml.CreateElement("DirCount");
            dirCount.InnerText = ItemCount.ToString();
            item.AppendChild(dirCount);

            return item;
        }

        public void SetItemCount(int itemCount)
        {
            this.ItemCount = itemCount;
        }
    }
}
