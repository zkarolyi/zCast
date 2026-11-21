using Helpers;

namespace Browser
{
    public class RadioBrowser
    {
        public static string API_ENDPOINT = "http://all.api.radio-browser.info";
        public static string CLIENT_NAME = "RadioBrowserClientName";

        private const int MINIMUM_COUNT_GENRE = 5;
        private const int MINIMUM_COUNT_COUNTRY = 5;
        private const int MINIMUM_COUNT_LANGUAGE = 5;
        private const int DEFAULT_STATION_LIMIT = 200;
        private const bool SHOW_BROKEN_STATIONS = false;
        public const string ID_PREFIX = "RB";
        private readonly ILogger<RadioBrowser> logger;
        private readonly HttpClient httpClient;

        public RadioBrowser(ILogger<RadioBrowser> logger, IHttpClientFactory httpClientFactory)
        {
            this.logger = logger;
            this.httpClient = httpClientFactory.CreateClient(CLIENT_NAME);
        }

        public static string GetJsonAttr(Dictionary<string, string> json, string attr)
        {
            if (json.TryGetValue(attr, out var value) && value != null)
            {
                return value;
            }
            return string.Empty;
        }
        //def get_json_attr(json, attr):
        //    try:
        //        return json[attr]
        //    except:
        //return None

        public string GetPlayableUrl(string Id)
        {
            try
            {
                var playableUrlJson = Request("url/" + Generic.GetStationidWithoutPrefix(Id)).FirstOrDefault();
                if (playableUrlJson != null && playableUrlJson.ContainsKey("url"))
                {
                    return playableUrlJson["url"];
                }
                else
                {
                    logger.LogInformation("Could not retrieve the playable URL for station with id '{Id}'", Id);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Error while retrieving playable URL for station with id '{Id}': {Message}", Id, ex.Message);
            }
            return string.Empty;
        }
        //    def get_playable_url(self):
        //        try:
        //            playable_url_json = request('url/' + generic.get_stationid_without_prefix(self.id))[0]
        //            self.url = playable_url_json['url']
        //        except(IndexError, KeyError):
        //            logging.error("Could not retrieve first playlist item for station with id '%s'", self.id)


        public List<Dictionary<string, string>> Request(string url)
        {
            logger.LogInformation("Radiobrowser API request: {url}", url);
            try
            {
                var response = httpClient.GetAsync("/json/" + url).Result;

                if (response.IsSuccessStatusCode)
                {
                    var responseData = response.Content.ReadAsStringAsync().Result;
                    // Detect array vs. single object by the first character, not by any '[' in the payload
                    // (station names/URLs can contain '[').
                    if (!responseData.TrimStart().StartsWith("["))
                    {
                        var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(responseData ?? string.Empty) ?? new Dictionary<string, string>();
                        return new List<Dictionary<string, string>> { data };
                    }
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(responseData ?? string.Empty) ?? new List<Dictionary<string, string>>();
                }
                else
                {
                    logger.LogWarning("Could not fetch data from Radiobrowser API (HTTP status {StatusCode})", response.StatusCode);
                    return new List<Dictionary<string, string>>();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Connection to Radiobrowser API failed: {Message}", ex.Message);
                return new List<Dictionary<string, string>>();
            }
        }
        //def request(url):
        //    logging.debug("Radiobrowser API request: %s", url)
        //    headers = { 'content-type': 'application/json', 'User-Agent': generic.USER_AGENT + '/' + __version__}
        //try:
        //        response = requests.get(API_ENDPOINT + '/json/' + url, headers = headers)
        //    except requests.exceptions.ConnectionError as err:
        //        logging.error("Connection to Radiobrowser API failed (%s)", err)
        //        return { }
        //if response.status_code != 200:
        //        logging.error("Could not fetch data from Radiobrowser API (HTML status %s)", response.status_code)
        //        return { }
        //return response.json()

        public BrowserStation? GetStationById(string uid)
        {
            var stationJson = Request("stations/byuuid/" + Uri.EscapeDataString(uid)).FirstOrDefault();
            if (stationJson != null && !string.IsNullOrEmpty(GetJsonAttr(stationJson, "stationuuid")))
            {
                return new BrowserStation(stationJson);
            }
            return null;
        }
        //def get_station_by_id(uid):
        //    station_json = request('stations/byuuid/' + str(uid))
        //    if station_json and len(station_json):
        //        return Station(station_json[0])
        //    else:
        //        return None

        public List<BrowserStation> Search(string name, int limit = DEFAULT_STATION_LIMIT)
        {
            var stations = new List<BrowserStation>();
            var stationsJson = Request("stations/search?order=name&reverse=false&limit=" + limit + "&name=" + Uri.EscapeDataString(name));
            foreach (var stationJson in stationsJson)
            {
                if (SHOW_BROKEN_STATIONS || GetJsonAttr(stationJson, "lastcheckok") == "1")
                {
                    stations.Add(new BrowserStation(stationJson));
                }
            }
            return stations;
        }
        //def search(name, limit= DEFAULT_STATION_LIMIT):
        //    stations = []
        //    stations_json = request('stations/search?order=name&reverse=false&limit=' + str(limit) + '&name=' + str(name))
        //    for station_json in stations_json:
        //        if SHOW_BROKEN_STATIONS or get_json_attr(station_json, 'lastcheckok') == 1:
        //            stations.append(Station(station_json))
        //    return stations

        public List<RadioDirectory> GetCountryDirectories()
        {
            var countryDirectories = new List<RadioDirectory>();
            var apicall = "countries";
            if (!SHOW_BROKEN_STATIONS)
            {
                apicall += "?hidebroken=true";
            }
            var countriesRaw = Request(apicall);
            foreach (var countryRaw in countriesRaw)
            {
                if (countryRaw.ContainsKey("name") && countryRaw.ContainsKey("stationcount") &&
                    int.Parse(countryRaw["stationcount"]) > MINIMUM_COUNT_COUNTRY)
                {
                    countryDirectories.Add(new RadioDirectory(countryRaw["name"], int.Parse(countryRaw["stationcount"])));
                }
            }
            return countryDirectories;
        }
        //def get_country_directories():
        //    country_directories = []
        //    apicall = 'countries'
        //    if not SHOW_BROKEN_STATIONS:
        //    apicall += '?hidebroken=true'
        //    countries_raw = request(apicall)
        //    for country_raw in countries_raw:
        //        if get_json_attr(country_raw, 'name') and get_json_attr(country_raw, 'stationcount') and \
        //                int(get_json_attr(country_raw, 'stationcount')) > MINIMUM_COUNT_COUNTRY:
        //            country_directories.append(generic.Directory(get_json_attr(country_raw, 'name'),
        //                                                         get_json_attr(country_raw, 'stationcount')))
        //    return country_directories

        public List<RadioDirectory> GetLanguageDirectories()
        {
            var languageDirectories = new List<RadioDirectory>();
            var apicall = "languages";
            if (!SHOW_BROKEN_STATIONS)
            {
                apicall += "?hidebroken=true";
            }
            var languagesRaw = Request(apicall);
            foreach (var languageRaw in languagesRaw)
            {
                if (languageRaw.ContainsKey("name") && languageRaw.ContainsKey("stationcount") &&
                    int.Parse(languageRaw["stationcount"]) > MINIMUM_COUNT_LANGUAGE)
                {
                    languageDirectories.Add(new RadioDirectory(languageRaw["name"], int.Parse(languageRaw["stationcount"]), languageRaw["name"]));
                }
            }
            return languageDirectories;
        }
        //def get_language_directories():
        //    language_directories = []
        //    apicall = 'languages'
        //    if not SHOW_BROKEN_STATIONS:
        //    apicall += '?hidebroken=true'
        //    languages_raw = request(apicall)
        //    for language_raw in languages_raw:
        //        if get_json_attr(language_raw, 'name') and get_json_attr(language_raw, 'stationcount') and \
        //                int(get_json_attr(language_raw, 'stationcount')) > MINIMUM_COUNT_LANGUAGE:
        //            language_directories.append(generic.Directory(get_json_attr(language_raw, 'name'),
        //                                                          get_json_attr(language_raw, 'stationcount'),
        //                                                          get_json_attr(language_raw, 'name').title()))
        //    return language_directories

        public List<RadioDirectory> GetGenreDirectories()
        {
            var genreDirectories = new List<RadioDirectory>();
            var apicall = "tags";
            if (!SHOW_BROKEN_STATIONS)
            {
                apicall += "?hidebroken=true";
            }
            var genresRaw = Request(apicall);
            foreach (var genreRaw in genresRaw)
            {
                if (genreRaw.ContainsKey("name") && genreRaw.ContainsKey("stationcount") &&
                    int.Parse(genreRaw["stationcount"]) > MINIMUM_COUNT_GENRE)
                {
                    genreDirectories.Add(new RadioDirectory(genreRaw["name"], int.Parse(genreRaw["stationcount"]), genreRaw["name"].ToUpper()));
                }
            }
            return genreDirectories;
        }
        //def get_genre_directories():
        //    genre_directories = []
        //    apicall = 'tags'
        //    if not SHOW_BROKEN_STATIONS:
        //    apicall += '?hidebroken=true'
        //    genres_raw = request(apicall)
        //    for genre_raw in genres_raw:
        //        if get_json_attr(genre_raw, 'name') and get_json_attr(genre_raw, 'stationcount') and \
        //                int(get_json_attr(genre_raw, 'stationcount')) > MINIMUM_COUNT_GENRE:
        //            genre_directories.append(generic.Directory(get_json_attr(genre_raw, 'name'),
        //                                                       get_json_attr(genre_raw, 'stationcount'),
        //                                                       get_json_attr(genre_raw, 'name').capitalize()))
        //    return genre_directories


        public List<BrowserStation> GetStationsByCountry(string country)
        {
            var stations = new List<BrowserStation>();
            var stationsJson = Request("stations/search?order=name&reverse=false&countryExact=true&country=" + Uri.EscapeDataString(country));
            foreach (var stationJson in stationsJson)
            {
                if (SHOW_BROKEN_STATIONS || GetJsonAttr(stationJson, "lastcheckok") == "1")
                {
                    stations.Add(new BrowserStation(stationJson));
                }
            }
            return stations;
        }
        //def get_stations_by_country(country):
        //    stations = []
        //    stations_json = request('stations/search?order=name&reverse=false&countryExact=true&country=' + str(country))
        //    for station_json in stations_json:
        //        if SHOW_BROKEN_STATIONS or get_json_attr(station_json, 'lastcheckok') == 1:
        //            stations.append(Station(station_json))
        //    return stations


        public List<BrowserStation> GetStationsByLanguage(string language)
        {
            var stations = new List<BrowserStation>();
            var stationsJson = Request("stations/search?order=name&reverse=false&languageExact=true&language=" + Uri.EscapeDataString(language));
            foreach (var stationJson in stationsJson)
            {
                if (SHOW_BROKEN_STATIONS || GetJsonAttr(stationJson, "lastcheckok") == "1")
                {
                    stations.Add(new BrowserStation(stationJson));
                }
            }
            return stations;
        }
        //def get_stations_by_language(language):
        //    stations = []
        //    stations_json = request('stations/search?order=name&reverse=false&languageExact=true&language=' + str(language))
        //    for station_json in stations_json:
        //        if SHOW_BROKEN_STATIONS or get_json_attr(station_json, 'lastcheckok') == 1:
        //            stations.append(Station(station_json))
        //    return stations

        public List<BrowserStation> GetStationsByGenre(string genre)
        {
            var stations = new List<BrowserStation>();
            var stationsJson = Request("stations/search?order=name&reverse=false&tagExact=true&tag=" + Uri.EscapeDataString(genre));
            foreach (var stationJson in stationsJson)
            {
                if (SHOW_BROKEN_STATIONS || GetJsonAttr(stationJson, "lastcheckok") == "1")
                {
                    stations.Add(new BrowserStation(stationJson));
                }
            }
            return stations;
        }
        //def get_stations_by_genre(genre):
        //    stations = []
        //    stations_json = request('stations/search?order=name&reverse=false&tagExact=true&tag=' + str(genre))
        //    for station_json in stations_json:
        //        if SHOW_BROKEN_STATIONS or get_json_attr(station_json, 'lastcheckok') == 1:
        //            stations.append(Station(station_json))
        //    return stations

        public List<BrowserStation> GetStationsByVotes(int limit = DEFAULT_STATION_LIMIT)
        {
            var stations = new List<BrowserStation>();
            var stationsJson = Request("stations?order=votes&reverse=true&limit=" + limit);
            foreach (var stationJson in stationsJson)
            {
                if (SHOW_BROKEN_STATIONS || GetJsonAttr(stationJson, "lastcheckok") == "1")
                {
                    stations.Add(new BrowserStation(stationJson));
                }
            }
            return stations;
        }
        //def get_stations_by_votes(limit= DEFAULT_STATION_LIMIT):
        //    stations = []
        //    stations_json = request('stations?order=votes&reverse=true&limit=' + str(limit))
        //    for station_json in stations_json:
        //        if SHOW_BROKEN_STATIONS or get_json_attr(station_json, 'lastcheckok') == 1:
        //            stations.append(Station(station_json))
        //    return stations
    }
}



//import requests
//import logging

//from ycast import __version__
//import ycast.vtuner as vtuner
//import ycast.generic as generic

//API_ENDPOINT = "http://all.api.radio-browser.info"
//MINIMUM_COUNT_GENRE = 5
//MINIMUM_COUNT_COUNTRY = 5
//MINIMUM_COUNT_LANGUAGE = 5
//DEFAULT_STATION_LIMIT = 200
//SHOW_BROKEN_STATIONS = False
//ID_PREFIX = "RB"


//def get_json_attr(json, attr):
//    try:
//        return json[attr]
//    except:
//return None


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

//    def to_vtuner(self):
//        return vtuner.Station(self.id, self.name, ', '.join(self.tags), self.url, self.icon,
//                              self.tags[0], self.countrycode, self.codec, self.bitrate, None)

//    def get_playable_url(self):
//        try:
//            playable_url_json = request('url/' + generic.get_stationid_without_prefix(self.id))[0]
//            self.url = playable_url_json['url']
//        except(IndexError, KeyError):
//            logging.error("Could not retrieve first playlist item for station with id '%s'", self.id)


//def request(url):
//    logging.debug("Radiobrowser API request: %s", url)
//    headers = { 'content-type': 'application/json', 'User-Agent': generic.USER_AGENT + '/' + __version__}
//try:
//        response = requests.get(API_ENDPOINT + '/json/' + url, headers = headers)
//    except requests.exceptions.ConnectionError as err:
//        logging.error("Connection to Radiobrowser API failed (%s)", err)
//        return { }
//if response.status_code != 200:
//        logging.error("Could not fetch data from Radiobrowser API (HTML status %s)", response.status_code)
//        return { }
//return response.json()


//def get_station_by_id(uid):
//    station_json = request('stations/byuuid/' + str(uid))
//    if station_json and len(station_json):
//        return Station(station_json[0])
//    else:
//        return None


//def search(name, limit= DEFAULT_STATION_LIMIT):
//    stations = []
//    stations_json = request('stations/search?order=name&reverse=false&limit=' + str(limit) + '&name=' + str(name))
//    for station_json in stations_json:
//        if SHOW_BROKEN_STATIONS or get_json_attr(station_json, 'lastcheckok') == 1:
//            stations.append(Station(station_json))
//    return stations


//def get_country_directories():
//    country_directories = []
//    apicall = 'countries'
//    if not SHOW_BROKEN_STATIONS:
//    apicall += '?hidebroken=true'
//    countries_raw = request(apicall)
//    for country_raw in countries_raw:
//        if get_json_attr(country_raw, 'name') and get_json_attr(country_raw, 'stationcount') and \
//                int(get_json_attr(country_raw, 'stationcount')) > MINIMUM_COUNT_COUNTRY:
//            country_directories.append(generic.Directory(get_json_attr(country_raw, 'name'),
//                                                         get_json_attr(country_raw, 'stationcount')))
//    return country_directories


//def get_language_directories():
//    language_directories = []
//    apicall = 'languages'
//    if not SHOW_BROKEN_STATIONS:
//    apicall += '?hidebroken=true'
//    languages_raw = request(apicall)
//    for language_raw in languages_raw:
//        if get_json_attr(language_raw, 'name') and get_json_attr(language_raw, 'stationcount') and \
//                int(get_json_attr(language_raw, 'stationcount')) > MINIMUM_COUNT_LANGUAGE:
//            language_directories.append(generic.Directory(get_json_attr(language_raw, 'name'),
//                                                          get_json_attr(language_raw, 'stationcount'),
//                                                          get_json_attr(language_raw, 'name').title()))
//    return language_directories


//def get_genre_directories():
//    genre_directories = []
//    apicall = 'tags'
//    if not SHOW_BROKEN_STATIONS:
//    apicall += '?hidebroken=true'
//    genres_raw = request(apicall)
//    for genre_raw in genres_raw:
//        if get_json_attr(genre_raw, 'name') and get_json_attr(genre_raw, 'stationcount') and \
//                int(get_json_attr(genre_raw, 'stationcount')) > MINIMUM_COUNT_GENRE:
//            genre_directories.append(generic.Directory(get_json_attr(genre_raw, 'name'),
//                                                       get_json_attr(genre_raw, 'stationcount'),
//                                                       get_json_attr(genre_raw, 'name').capitalize()))
//    return genre_directories


//def get_stations_by_country(country):
//    stations = []
//    stations_json = request('stations/search?order=name&reverse=false&countryExact=true&country=' + str(country))
//    for station_json in stations_json:
//        if SHOW_BROKEN_STATIONS or get_json_attr(station_json, 'lastcheckok') == 1:
//            stations.append(Station(station_json))
//    return stations


//def get_stations_by_language(language):
//    stations = []
//    stations_json = request('stations/search?order=name&reverse=false&languageExact=true&language=' + str(language))
//    for station_json in stations_json:
//        if SHOW_BROKEN_STATIONS or get_json_attr(station_json, 'lastcheckok') == 1:
//            stations.append(Station(station_json))
//    return stations


//def get_stations_by_genre(genre):
//    stations = []
//    stations_json = request('stations/search?order=name&reverse=false&tagExact=true&tag=' + str(genre))
//    for station_json in stations_json:
//        if SHOW_BROKEN_STATIONS or get_json_attr(station_json, 'lastcheckok') == 1:
//            stations.append(Station(station_json))
//    return stations


//def get_stations_by_votes(limit= DEFAULT_STATION_LIMIT):
//    stations = []
//    stations_json = request('stations?order=votes&reverse=true&limit=' + str(limit))
//    for station_json in stations_json:
//        if SHOW_BROKEN_STATIONS or get_json_attr(station_json, 'lastcheckok') == 1:
//            stations.append(Station(station_json))
//    return stations
