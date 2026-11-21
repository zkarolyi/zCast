using System.Xml;

namespace VTuner
{

    public abstract class PageItems
    {
        public abstract XmlElement ToXml(XmlDocument xml);
    }
}
