using System.Xml;

namespace VTuner
{
    public class Display : PageItems
    {
        public string Text { get; set; }

        public Display(string text)
        {
            this.Text = text;
        }

        public override XmlElement ToXml(XmlDocument xml)
        {
            var item = xml.CreateElement("Item");

            var itemType = xml.CreateElement("ItemType");
            itemType.InnerText = "Display";
            item.AppendChild(itemType);

            var display = xml.CreateElement("Display");
            display.InnerText = Text;
            item.AppendChild(display);

            return item;
        }
    }
}
