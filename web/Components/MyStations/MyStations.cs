using System.Security.Cryptography;
using YamlDotNet.Serialization;
using System.Text;
using Helpers;

namespace My
{
    public class MyStation
    {
        private readonly ILogger<MyStation> logger;
        private readonly string id;
        private readonly string name;
        private readonly string url;
        private readonly string tag;
        private readonly string icon;
        private readonly StationStore? store;

        public static string ID_PREFIX = "MY";

        public MyStation(ILogger<MyStation> logger, StationStore store)
        {
            this.logger = logger;
            this.store = store;
            this.id = string.Empty;
            this.name = string.Empty;
            this.url = string.Empty;
            this.tag = string.Empty;
            this.icon = string.Empty;
        }

        public MyStation(ILogger<MyStation> logger, string uid, string name, string url, string category)
        {
            this.logger = logger;
            this.id = Generic.GenerateStationidWithPrefix(uid, ID_PREFIX);
            this.name = name;
            this.url = url;
            this.tag = category;
            this.icon = string.Empty;
        }

        public VTuner.Station ToVtuner()
        {
            return new VTuner.Station(id, name, tag, url, icon, tag, string.Empty, string.Empty, 0, string.Empty);
        }

        public MyStation? GetStationById(string uid)
        {
            var myStationsYaml = GetStationsYaml();
            if (myStationsYaml != null)
            {
                foreach (var category in myStationsYaml.Categories)
                {
                    foreach (var station in GetStationsByCategory(category.Key))
                    {
                        if (uid == Generic.GetStationidWithoutPrefix(station.id))
                        {
                            return station;
                        }
                    }
                }
            }
            return null;
        }

        public RadioCategories GetStationsYaml()
        {
            // Station entries created internally have no store; only the DI instance reads the file.
            return store?.Load() ?? new RadioCategories();
        }

        public List<RadioDirectory> GetCategoryDirectories()
        {
            var myStationsYaml = GetStationsYaml();
            var categories = new List<RadioDirectory>();
            if (myStationsYaml != null && myStationsYaml.Categories.Count > 0)
            {
                foreach (var category in myStationsYaml.Categories)
                {
                    categories.Add(new RadioDirectory(category.Key, GetStationsByCategory(category.Key).Count));
                }
            }
            return categories;
        }

        public List<MyStation> GetStationsByCategory(string category)
        {
            var myStationsYaml = GetStationsYaml();
            var stations = new List<MyStation>();
            if (myStationsYaml != null && myStationsYaml.Categories.Count > 0)
            {
                foreach (var station in myStationsYaml.Categories.Where(t => t.Key == category).SelectMany(t => t.Value))
                {
                    var stationId = GetChecksum(station.Name + station.Url).ToUpper();

                    stations.Add(new MyStation(logger, stationId, station.Name, station.Url, category));
                }
            }
            return stations;
        }

        public string GetChecksum(string feed, int charLimit = 12)
        {
            using MD5 md5 = MD5.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(feed);
            byte[] hashBytes = md5.ComputeHash(inputBytes);
            byte[] xorFold = new byte[8];

            for (int i = 0; i < 8; i++)
            {
                xorFold[i] = hashBytes[i];
            }

            for (int i = 8; i < hashBytes.Length; i++)
            {
                xorFold[i % 8] ^= hashBytes[i];
            }

            string digestXorFold = BitConverter.ToString(xorFold).Replace("-", string.Empty).ToLower();
            return digestXorFold[..Math.Min(charLimit, digestXorFold.Length)];
        }
    }

    //public class RadioCategories
    //{
    //    public List<Category> Categories { get; set; } = new List<Category>();
    //}

    //public class Category
    //{
    //    public string Name { get; set; } = string.Empty;
    //    public List<Station> Stations { get; set; } = new List<Station>();
    //}

    //public class Station
    //{
    //    public string Name { get; set; } = string.Empty;
    //    public string Url { get; set; } = string.Empty;
    //}
}


//import logging
//import hashlib

//import yaml

//import ycast.vtuner as vtuner
//import ycast.generic as generic

//ID_PREFIX = "MY"

//config_file = 'stations.yml'


//class Station :
//    def __init__(self, uid, name, url, category):
//        self.id = generic.generate_stationid_with_prefix(uid, ID_PREFIX)
//        self.name = name
//        self.url = url
//        self.tag = category
//        self.icon = None

//    def to_vtuner(self):
//        return vtuner.Station(self.id, self.name, self.tag, self.url, self.icon, self.tag, None, None, None, None)


//def set_config(config):
//    global config_file
//    if config:
//        config_file = config
//    if get_stations_yaml():
//        return True
//    else:
//        return False


//def get_station_by_id(uid):
//    my_stations_yaml = get_stations_yaml()
//    if my_stations_yaml:
//        for category in my_stations_yaml:
//            for station in get_stations_by_category(category):
//                if uid == generic.get_stationid_without_prefix(station.id):
//                    return station
//    return None


//def get_stations_yaml():
//    try:
//        with open(config_file, 'r') as f:
//            my_stations = yaml.safe_load(f)
//    except FileNotFoundError:
//        logging.error("Station configuration '%s' not found", config_file)
//        return None
//    except yaml.YAMLError as e:
//        logging.error("Station configuration format error: %s", e)
//        return None
//    return my_stations


//def get_category_directories():
//    my_stations_yaml = get_stations_yaml()
//    categories = []
//    if my_stations_yaml:
//        for category in my_stations_yaml:
//            categories.append(generic.Directory(category, len(get_stations_by_category(category))))
//    return categories


//def get_stations_by_category(category):
//    my_stations_yaml = get_stations_yaml()
//    stations = []
//    if my_stations_yaml and category in my_stations_yaml:
//for station_name in my_stations_yaml[category]:

//    station_url = my_stations_yaml[category][station_name]

//    station_id = str(get_checksum(station_name + station_url)).upper()

//    stations.append(Station(station_id, station_name, station_url, category))
//    return stations


//def get_checksum(feed, charlimit= 12):
//    hash_feed = feed.encode()
//    hash_object = hashlib.md5(hash_feed)
//    digest = hash_object.digest()
//    xor_fold = bytearray(digest[:8])
//    for i, b in enumerate(digest[8:]):
//        xor_fold[i] ^= b
//    digest_xor_fold = ''.join(format(x, '02x') for x in bytes(xor_fold))
//        return digest_xor_fold[:charlimit]
