namespace Helpers
{
    public class RadioDirectory
    {
        public readonly string name;
        public readonly int itemCount;
        public readonly string displayname;

        public RadioDirectory(string name, int itemCount, string displayname = "")
        {
            this.name = name;
            this.itemCount = itemCount;
            if (string.IsNullOrEmpty(displayname))
            {
                this.displayname = name;
            }
            else
            {
                this.displayname = displayname;
            }
        }
    }
}
