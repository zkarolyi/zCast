using Browser;
using Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using My;
using System.Text.RegularExpressions;
using VTuner;

namespace web.Controllers
{
    [Route("ycast")]
    [ApiController]
    public class YCastController : ControllerBase
    {
        private bool myStationsEnabled = true;
        private readonly bool stationTracking = false;
        const string PATH_ROOT = "/ycast";
        const string PATH_PLAY = "play";
        const string PATH_STATION = "station";
        const string PATH_SEARCH = "search";
        const string PATH_ICON = "icon";
        const string PATH_MY_STATIONS = "my_stations";
        const string PATH_RADIOBROWSER = "radiobrowser";
        const string PATH_RADIOBROWSER_COUNTRY = "country";
        const string PATH_RADIOBROWSER_LANGUAGE = "language";
        const string PATH_RADIOBROWSER_GENRE = "genre";
        const string PATH_RADIOBROWSER_POPULAR = "popular";
        private readonly ILogger<YCastController> logger;
        private readonly MyStation myStation;
        private readonly RadioBrowser radioBrowser;
        private readonly IconFetcher iconFetcher;

        public YCastController(ILogger<YCastController> logger, MyStation myStation, RadioBrowser radioBrowser, IconFetcher iconFetcher)
        {
            this.logger = logger;
            this.myStation = myStation;
            this.radioBrowser = radioBrowser;
            this.iconFetcher = iconFetcher;
        }

        [HttpGet("/")]
        [HttpGet]
        public string Landing()
        {
            var page = new VTuner.Page();
            //page.Add(new VTuner.Directory("Radiobrowser", Request.Scheme + "://" + Request.Host.Value + PATH_ROOT + "/" + PATH_RADIOBROWSER, 4));
            page.Add(new VTuner.Directory("Radiobrowser", Request.BaseUrl(PATH_ROOT, PATH_RADIOBROWSER), 4));
            if (myStationsEnabled)
            {
                page.Add(new VTuner.Directory("My Stations", Request.BaseUrl(PATH_ROOT, PATH_MY_STATIONS), myStation.GetCategoryDirectories().Count));
            }
            else
            {
                page.Add(new VTuner.Display("'My Stations' feature not configured."));
                page.SetCount(1);
            }
            return page.ToString();
        }
        //@app.route('/',
        //           defaults ={ 'path': ''},
        //           methods = ['GET', 'POST'])
        //@app.route('/' + PATH_ROOT + '/',
        //           defaults ={ 'path': ''},
        //           methods = ['GET', 'POST'])
        //def landing(path= ''):
        //    page = vtuner.Page()
        //    page.add(vtuner.Directory('Radiobrowser', url_for('radiobrowser_landing', _external = True), 4))
        //    if my_stations_enabled:
        //        page.add(vtuner.Directory('My Stations', url_for('my_stations_landing', _external = True),
        //                                  len(my_stations.get_category_directories())))
        //    else:
        //        page.add(vtuner.Display("'My Stations' feature not configured."))
        //        page.set_count(1)
        //    return page.to_string()

        [HttpGet(PATH_MY_STATIONS)]
        public ActionResult MyStationsLanding()
        {
            var directories = myStation.GetCategoryDirectories();
            return Ok(GetDirectoriesPage(PATH_MY_STATIONS, directories, Request).ToString());
        }
        //@app.route('/' + PATH_ROOT + '/' + PATH_MY_STATIONS + '/',
        //           methods = ['GET', 'POST'])
        //def my_stations_landing():
        //    directories = my_stations.get_category_directories()
        //    return get_directories_page('my_stations_category', directories, request).to_string()

        [HttpGet(PATH_MY_STATIONS + "/{directory}")]
        public string MyStationsCategory(string directory)
        {
            var stations = myStation.GetStationsByCategory(directory).Select(s => s.ToVtuner()).ToList();
            return GetStationsPage(stations, Request).ToString();
        }
        //@app.route('/' + PATH_ROOT + '/' + PATH_MY_STATIONS + '/<directory>',
        //           methods = ['GET', 'POST'])
        //def my_stations_category(directory):
        //    stations = my_stations.get_stations_by_category(directory)
        //    return get_stations_page(stations, request).to_string()

        [HttpGet(PATH_RADIOBROWSER)]
        public string RadiobrowserLanding()
        {
            var page = new VTuner.Page();
            page.Add(new VTuner.Directory("Genres", Request.BaseUrl(PATH_ROOT + "/" + PATH_RADIOBROWSER, PATH_RADIOBROWSER_GENRE), radioBrowser.GetGenreDirectories().Count));
            page.Add(new VTuner.Directory("Countries", Request.BaseUrl(PATH_ROOT + "/" + PATH_RADIOBROWSER, PATH_RADIOBROWSER_COUNTRY), radioBrowser.GetCountryDirectories().Count));
            page.Add(new VTuner.Directory("Languages", Request.BaseUrl(PATH_ROOT + "/" + PATH_RADIOBROWSER, PATH_RADIOBROWSER_LANGUAGE), radioBrowser.GetLanguageDirectories().Count));
            page.Add(new VTuner.Directory("Most Popular", Request.BaseUrl(PATH_ROOT + "/" + PATH_RADIOBROWSER, PATH_RADIOBROWSER_POPULAR), radioBrowser.GetStationsByVotes().Count));
            page.SetCount(4);
            return page.ToString();
        }
        //@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/',
        //           methods = ['GET', 'POST'])
        //def radiobrowser_landing():
        //    page = vtuner.Page()
        //    page.add(vtuner.Directory('Genres', url_for('radiobrowser_genres', _external = True),
        //                              len(radiobrowser.get_genre_directories())))
        //    page.add(vtuner.Directory('Countries', url_for('radiobrowser_countries', _external = True),
        //                              len(radiobrowser.get_country_directories())))
        //    page.add(vtuner.Directory('Languages', url_for('radiobrowser_languages', _external = True),
        //                              len(radiobrowser.get_language_directories())))
        //    page.add(vtuner.Directory('Most Popular', url_for('radiobrowser_popular', _external = True),
        //                              len(radiobrowser.get_stations_by_votes())))
        //    page.set_count(4)
        //    return page.to_string()

        [HttpGet(PATH_RADIOBROWSER + "/" + PATH_RADIOBROWSER_COUNTRY)]
        public string RadiobrowserCountries()
        {
            var directories = radioBrowser.GetCountryDirectories();
            return GetDirectoriesPage(PATH_RADIOBROWSER + "/" + PATH_RADIOBROWSER_COUNTRY, directories, Request).ToString();
        }
        //@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/' + PATH_RADIOBROWSER_COUNTRY + '/',
        //           methods = ['GET', 'POST'])
        //def radiobrowser_countries():
        //    directories = radiobrowser.get_country_directories()
        //    return get_directories_page('radiobrowser_country_stations', directories, request).to_string()

        [HttpGet(PATH_RADIOBROWSER + "/" + PATH_RADIOBROWSER_COUNTRY + "/{directory}")]
        public string RadiobrowserCountryStations(string directory)
        {
            var stations = radioBrowser.GetStationsByCountry(directory).Select(s => s.ToVtuner()).ToList();
            return GetStationsPage(stations, Request).ToString();
        }
        //@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/' + PATH_RADIOBROWSER_COUNTRY + '/<directory>',
        //           methods = ['GET', 'POST'])
        //def radiobrowser_country_stations(directory):
        //    stations = radiobrowser.get_stations_by_country(directory)
        //    return get_stations_page(stations, request).to_string()

        [HttpGet(PATH_RADIOBROWSER + "/" + PATH_RADIOBROWSER_LANGUAGE)]
        public string RadiobrowserLanguages()
        {
            var directories = radioBrowser.GetLanguageDirectories();
            return GetDirectoriesPage(PATH_RADIOBROWSER + "/" + PATH_RADIOBROWSER_LANGUAGE, directories, Request).ToString();
        }
        //@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/' + PATH_RADIOBROWSER_LANGUAGE + '/',
        //           methods = ['GET', 'POST'])
        //def radiobrowser_languages():
        //    directories = radiobrowser.get_language_directories()
        //    return get_directories_page('radiobrowser_language_stations', directories, request).to_string()

        [HttpGet(PATH_RADIOBROWSER + "/" + PATH_RADIOBROWSER_LANGUAGE + "/{directory}")]
        public string RadiobrowserLanguageStations(string directory)
        {
            var stations = radioBrowser.GetStationsByLanguage(directory).Select(s => s.ToVtuner()).ToList();
            return GetStationsPage(stations, Request).ToString();
        }
        //@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/' + PATH_RADIOBROWSER_LANGUAGE + '/<directory>',
        //           methods = ['GET', 'POST'])
        //def radiobrowser_language_stations(directory):
        //    stations = radiobrowser.get_stations_by_language(directory)
        //    return get_stations_page(stations, request).to_string()

        [HttpGet(PATH_RADIOBROWSER + "/" + PATH_RADIOBROWSER_GENRE)]
        public string RadiobrowserGenres()
        {
            var directories = radioBrowser.GetGenreDirectories();
            return GetDirectoriesPage(PATH_RADIOBROWSER + "/" + PATH_RADIOBROWSER_GENRE, directories, Request).ToString();
        }
        //@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/' + PATH_RADIOBROWSER_GENRE + '/',
        //           methods = ['GET', 'POST'])
        //def radiobrowser_genres():
        //    directories = radiobrowser.get_genre_directories()
        //    return get_directories_page('radiobrowser_genre_stations', directories, request).to_string()

        [HttpGet(PATH_RADIOBROWSER + "/" + PATH_RADIOBROWSER_GENRE + "/{directory}")]
        public string RadiobrowserGenreStations(string directory)
        {
            var stations = radioBrowser.GetStationsByGenre(directory).Select(s => s.ToVtuner()).ToList();
            return GetStationsPage(stations, Request).ToString();
        }
        //@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/' + PATH_RADIOBROWSER_GENRE + '/<directory>',
        //           methods = ['GET', 'POST'])
        //def radiobrowser_genre_stations(directory):
        //    stations = radiobrowser.get_stations_by_genre(directory)
        //    return get_stations_page(stations, request).to_string()

        [HttpGet(PATH_RADIOBROWSER + "/" + PATH_RADIOBROWSER_POPULAR)]
        public string RadiobrowserPopular()
        {
            var stations = radioBrowser.GetStationsByVotes().Select(s => s.ToVtuner()).ToList();
            return GetStationsPage(stations, Request).ToString();
        }
        //@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/' + PATH_RADIOBROWSER_POPULAR + '/',
        //           methods = ['GET', 'POST'])
        //def radiobrowser_popular():
        //    stations = radiobrowser.get_stations_by_votes()
        //    return get_stations_page(stations, request).to_string()

        [HttpGet(PATH_SEARCH)]
        public string StationSearch([FromQuery(Name = "search")] string? query)
        {
            if (string.IsNullOrEmpty(query) || query.Length < 3)
            {
                var page = new VTuner.Page();
                page.Add(new VTuner.Display("Search query too short"));
                page.SetCount(1);
                return page.ToString();
            }
            else
            {
                // TODO: we also need to include 'my station' elements
                var stations = radioBrowser.Search(query).Select(s => s.ToVtuner()).ToList();
                return GetStationsPage(stations, Request).ToString();
            }
        }
        //@app.route('/' + PATH_ROOT + '/' + PATH_SEARCH + '/',
        //           methods = ['GET', 'POST'])
        //def station_search():
        //    query = request.args.get('search')
        //    if not query or len(query) < 3:
        //        page = vtuner.Page()
        //        page.add(vtuner.Display("Search query too short"))
        //        page.set_count(1)
        //        return page.to_string()
        //    else:
        //        # TODO: we also need to include 'my station' elements
        //        stations = radiobrowser.search(query)
        //        return get_stations_page(stations, request).to_string()

        [HttpGet("play")]
        public ActionResult GetStreamUrl(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("Stream URL without station ID requested");
            }
            var station = GetStationById(id, true);
            if (station == null || string.IsNullOrEmpty(station.Url))
            {
                return NotFound($"Could not get station with id '{id}'");
            }
            return VtunerRedirect(station.Url, Request);
        }
        //@app.route('/' + PATH_ROOT + '/' + PATH_PLAY,
        //           methods = ['GET', 'POST'])
        //def get_stream_url():
        //    stationid = request.args.get('id')
        //    if not stationid:
        //    logging.error("Stream URL without station ID requested")
        //        abort(400)
        //    station = get_station_by_id(stationid, additional_info = True)
        //    if not station:
        //    logging.error("Could not get station with id '%s'", stationid)
        //        abort(404)
        //    logging.debug("Station with ID '%s' requested", station.id)
        //    return vtuner_redirect(station.url)

        [HttpGet(PATH_STATION)]
        public ActionResult GetStationInfo(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                var page = new VTuner.Page();
                page.Add(new VTuner.Display("Station not found"));
                page.SetCount(1);
                return Ok(page.ToString());
            }
            var station = GetStationById(id, !stationTracking);
            if (station == null)
            {
                return NotFound($"Could not get station with id '{id}'");
            }
            if (stationTracking)
            {
                station.SetTrackUrl(Request.BaseUrl(PATH_ROOT, PATH_PLAY, "?id=" + station.Uid));
            }
            station.Icon = Request.BaseUrl(PATH_ROOT, PATH_ICON, "?id=" + station.Uid);
            var pageResponse = new VTuner.Page();
            pageResponse.Add(station);
            pageResponse.SetCount(1);
            return Ok(pageResponse.ToString());
        }
        //@app.route('/' + PATH_ROOT + '/' + PATH_STATION,
        //           methods = ['GET', 'POST'])
        //def get_station_info():
        //    stationid = request.args.get('id')
        //    if not stationid:
        //    logging.error("Station info without station ID requested")
        //        abort(400)
        //    station = get_station_by_id(stationid, additional_info = (not station_tracking))
        //    if not station:
        //    logging.error("Could not get station with id '%s'", stationid)
        //        page = vtuner.Page()
        //        page.add(vtuner.Display("Station not found"))
        //        page.set_count(1)
        //        return page.to_string()
        //    vtuner_station = station.to_vtuner()
        //    if station_tracking:
        //        vtuner_station.set_trackurl(request.host_url + PATH_ROOT + '/' + PATH_PLAY + '?id=' + vtuner_station.uid)
        //    vtuner_station.icon = request.host_url + PATH_ROOT + '/' + PATH_ICON + '?id=' + vtuner_station.uid
        //    page = vtuner.Page()
        //    page.add(vtuner_station)
        //    page.set_count(1)
        //    return page.to_string()

        [HttpGet("icon")]
        public async Task<ActionResult> GetStationIconAsync(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return BadRequest("Station icon without station ID requested");
            }
            var station = GetStationById(id);
            if (station == null || string.IsNullOrEmpty(station.Icon))
            {
                return NotFound($"Could not get station icon with id '{id}'");
            }
            var stationIcon = await iconFetcher.GetIconAsync(station);
            if (stationIcon == null || stationIcon.Length == 0)
            {
                return NotFound($"Could not get station icon for station with id '{id}'");
            }
            return File(stationIcon, "image/jpeg");
        }
        //@app.route('/' + PATH_ROOT + '/' + PATH_ICON,
        //           methods = ['GET', 'POST'])
        //def get_station_icon():
        //    stationid = request.args.get('id')
        //    if not stationid:
        //    logging.error("Station icon without station ID requested")
        //        abort(400)
        //    station = get_station_by_id(stationid)
        //    if not station:
        //    logging.error("Could not get station with id '%s'", stationid)
        //        abort(404)
        //    if not hasattr(station, 'icon') or not station.icon:
        //        logging.warning("No icon information found for station with id '%s'", stationid)
        //        abort(404)
        //    station_icon = station_icons.get_icon(station)
        //    if not station_icon:
        //    logging.error("Could not get station icon for station with id '%s'", stationid)
        //        abort(404)
        //    response = make_response(station_icon)
        //    response.headers.set('Content-Type', 'image/jpeg')
        //    return response

        //def check_my_stations_feature(config):
        //    global my_stations_enabled
        //    my_stations_enabled = my_stations.set_config(config)


        private VTuner.Page GetDirectoriesPage(string controllerPath, List<RadioDirectory> directories, HttpRequest request)
        {
            var page = new VTuner.Page();
            if (directories.Count == 0)
            {
                page.Add(new VTuner.Display("No entries found"));
                page.SetCount(1);
                return page;
            }
            foreach (var directory in GetPagedElements(directories, request.Query))
            {
                var vtunerDirectory = new VTuner.Directory(
                    directory.displayname,
                    request.BaseUrl(PATH_ROOT, $"{controllerPath}/{directory.name}"),
                    directory.itemCount);
                page.Add(vtunerDirectory);
            }
            page.SetCount(directories.Count);
            return page;
        }
        //def get_directories_page(subdir, directories, request):
        //    page = vtuner.Page()
        //    if len(directories) == 0:
        //        page.add(vtuner.Display("No entries found"))
        //        page.set_count(1)
        //        return page
        //    for directory in get_paged_elements(directories, request.args):
        //        vtuner_directory = vtuner.Directory(directory.displayname,
        //                                            url_for(subdir, _external = True, directory = directory.name),
        //                                            directory.item_count)
        //        page.add(vtuner_directory)
        //    page.set_count(len(directories))
        //    return page

        private Page GetStationsPage(List<VTuner.Station> stations, HttpRequest request)
        {
            var page = new Page();
            if (stations.Count == 0)
            {
                page.Add(new Display("No stations found"));
                page.SetCount(1);
                return page;
            }
            foreach (var station in GetPagedElements(stations, request.Query))
            {
                if (stationTracking)
                {
                    station.SetTrackUrl(request.BaseUrl(PATH_ROOT, PATH_PLAY, "?id=" + station.Uid));
                }
                station.Icon = request.BaseUrl(PATH_ROOT, PATH_ICON, "?id=" + station.Uid);
                page.Add(station);
            }
            page.SetCount(stations.Count);
            return page;
        }
        //def get_stations_page(stations, request):
        //    page = vtuner.Page()
        //    if len(stations) == 0:
        //        page.add(vtuner.Display("No stations found"))
        //        page.set_count(1)
        //        return page
        //    for station in get_paged_elements(stations, request.args):
        //        vtuner_station = station.to_vtuner()
        //        if station_tracking:
        //            vtuner_station.set_trackurl(request.host_url + PATH_ROOT + '/' + PATH_PLAY + '?id=' + vtuner_station.uid)
        //        vtuner_station.icon = request.host_url + PATH_ROOT + '/' + PATH_ICON + '?id=' + vtuner_station.uid
        //        page.add(vtuner_station)
        //    page.set_count(len(stations))
        //    return page

        public List<T> GetPagedElements<T>(List<T> items, IQueryCollection query)
        {
            int offset;
            int limit;
            if (!string.IsNullOrEmpty(query["startitems"]) && int.TryParse(query["startitems"], out int startItems))
            {
                offset = startItems - 1;
            }
            else if (!string.IsNullOrEmpty(query["startItems"]) && int.TryParse(query["startItems"], out int startItems2))
            {
                offset = startItems2 - 1;
            }
            else if (!string.IsNullOrEmpty(query["start"]) && int.TryParse(query["start"], out int start))
            {
                offset = start - 1;
            }
            else
            {
                offset = 0;
            }

            if (offset < 0)
            {
                offset = 0;
            }

            if (offset > items.Count)
            {
                logger.LogInformation("Paging offset larger than item count");
                return new List<T>();
            }

            if (!string.IsNullOrEmpty(query["enditems"]) && int.TryParse(query["enditems"], out int endItems))
            {
                limit = endItems;
            }
            else if (!string.IsNullOrEmpty(query["endItems"]) && int.TryParse(query["endItems"], out int endItems2))
            {
                limit = endItems2;
            }
            else if (!string.IsNullOrEmpty(query["start"]) && !string.IsNullOrEmpty(query["howmany"]) && int.TryParse(query["start"], out int start) && int.TryParse(query["howmany"], out int howMany))
            {
                limit = start - 1 + howMany;
            }
            else
            {
                limit = items.Count;
            }

            if (limit < offset)
            {
                logger.LogInformation("Paging limit smaller than offset");
                return new List<T>();
            }

            if (limit > items.Count)
            {
                limit = items.Count;
            }

            return items.GetRange(offset, limit - offset);
        }
        //def get_paged_elements(items, requestargs):
        //    if requestargs.get('startitems'):
        //        offset = int(requestargs.get('startitems')) - 1
        //    elif requestargs.get('startItems'):
        //        offset = int(requestargs.get('startItems')) - 1
        //    elif requestargs.get('start'):
        //        offset = int(requestargs.get('start')) - 1
        //    else:
        //        offset = 0
        //    if offset > len(items):
        //        logging.warning("Paging offset larger than item count")
        //        return []
        //    if requestargs.get('enditems'):
        //        limit = int(requestargs.get('enditems'))
        //    elif requestargs.get('endItems'):
        //        limit = int(requestargs.get('endItems'))
        //    elif requestargs.get('start') and requestargs.get('howmany'):
        //        limit = int(requestargs.get('start')) - 1 + int(requestargs.get('howmany'))
        //    else:
        //        limit = len(items)
        //    if limit < offset:
        //        logging.warning("Paging limit smaller than offset")
        //        return []
        //    if limit > len(items):
        //        limit = len(items)
        //    return items[offset: limit]

        private VTuner.Station? GetStationById(string stationid, bool additional_info = false)
        {
            string stationIdPrefix = Generic.GetStationidPrefix(stationid);
            if (stationIdPrefix == MyStation.ID_PREFIX)
            {
                var station = myStation.GetStationById(Generic.GetStationidWithoutPrefix(stationid));
                if (station != null)
                {
                    return station.ToVtuner();
                }
            }
            if (stationIdPrefix == RadioBrowser.ID_PREFIX)
            {
                var station = radioBrowser.GetStationById(Generic.GetStationidWithoutPrefix(stationid));
                if (station == null)
                {
                    return null;
                }
                if (additional_info)
                {
                    // Keep the original URL if the playable URL can't be resolved (same as YCast).
                    var playableUrl = radioBrowser.GetPlayableUrl(stationid);
                    if (!string.IsNullOrEmpty(playableUrl))
                    {
                        station.Url = playableUrl;
                    }
                }
                return station.ToVtuner();
            }
            return null;
        }
        //def get_station_by_id(stationid, additional_info= False):
        //    station_id_prefix = generic.get_stationid_prefix(stationid)
        //    if station_id_prefix == my_stations.ID_PREFIX:
        //        return my_stations.get_station_by_id(generic.get_stationid_without_prefix(stationid))
        //    elif station_id_prefix == radiobrowser.ID_PREFIX:
        //        station = radiobrowser.get_station_by_id(generic.get_stationid_without_prefix(stationid))
        //        if additional_info:
        //            station.get_playable_url()
        //        return station
        //    return None

        private ActionResult VtunerRedirect(string url, HttpRequest request)
        {
            if (request != null && !string.IsNullOrEmpty(request.Host.Value) && !Regex.IsMatch(request.Host.Value, "^[A-Za-z0-9]+\\.vtuner\\.com$"))
            {
                logger.LogWarning("You are not accessing a YCast redirect with a whitelisted host url (*.vtuner.com). Some AVRs have problems with this. The requested host was: {Host}", request.Host.Value);
            }
            return new RedirectResult(url);
        }
        //def vtuner_redirect(url):
        //    if request and request.host and not re.search("^[A-Za-z0-9]+\.vtuner\.com$", request.host):
        //        logging.warning("You are not accessing a YCast redirect with a whitelisted host url (*.vtuner.com). "
        //                        "Some AVRs have problems with this. The requested host was: %s", request.host)
        //    return redirect(url, code = 302)

        // AVRs call this at the server root (e.g. /setupapp/Yamaha/asp/BrowseXML/loginXML.asp?token=0),
        // so the route must be absolute and match multiple path segments.
        [HttpGet("/setupapp/{**path}")]
        [HttpPost("/setupapp/{**path}")]
        public ActionResult SetupApp(string path)
        {
            path ??= string.Empty;
            if (Request.Query["token"] == "0")
            {
                return Ok(Page.GetInitToken());
            }
            string? search = Request.Query["search"];
            if (!string.IsNullOrEmpty(search))
            {
                return Ok(StationSearch(search));
            }
            string? id = Request.Query["id"];
            if (path.Contains("statxml.asp", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(id))
            {
                return GetStationInfo(id);
            }
            if (path.Contains("navXML.asp", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(RadiobrowserLanding());
            }
            if (path.Contains("FavXML.asp", StringComparison.OrdinalIgnoreCase))
            {
                return MyStationsLanding();
            }
            if (path.Contains("loginXML.asp", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(Landing());
            }
            logger.LogWarning("Unhandled upstream query (/setupapp/{Path})", path);
            return NotFound();
        }
        //@app.route('/setupapp/<path:path>',
        //           methods = ['GET', 'POST'])
        //def upstream(path):
        //    if request.args.get('token') == '0':
        //        return vtuner.get_init_token()
        //    if request.args.get('search'):
        //        return station_search()
        //    if 'statxml.asp' in path and request.args.get('id'):
        //        return get_station_info()
        //    if 'navXML.asp' in path:
        //return radiobrowser_landing()
        //    if 'FavXML.asp' in path:
        //return my_stations_landing()
        //    if 'loginXML.asp' in path:
        //return landing()
        //    logging.error("Unhandled upstream query (/setupapp/%s)", path)
        //    abort(404)
    }
}




//import logging
//import re

//from flask import Flask, request, url_for, redirect, abort, make_response

//import ycast.vtuner as vtuner
//import ycast.radiobrowser as radiobrowser
//import ycast.my_stations as my_stations
//import ycast.generic as generic
//import ycast.station_icons as station_icons


//PATH_ROOT = 'ycast'
//PATH_PLAY = 'play'
//PATH_STATION = 'station'
//PATH_SEARCH = 'search'
//PATH_ICON = 'icon'
//PATH_MY_STATIONS = 'my_stations'
//PATH_RADIOBROWSER = 'radiobrowser'
//PATH_RADIOBROWSER_COUNTRY = 'country'
//PATH_RADIOBROWSER_LANGUAGE = 'language'
//PATH_RADIOBROWSER_GENRE = 'genre'
//PATH_RADIOBROWSER_POPULAR = 'popular'

//station_tracking = False
//my_stations_enabled = False
//app = Flask(__name__)


//def run(config, address= '0.0.0.0', port= 8010):
//    try:
//        check_my_stations_feature(config)
//        app.run(host = address, port = port)
//    except PermissionError:
//        logging.error("No permission to create socket. Are you trying to use ports below 1024 without elevated rights?")


//def check_my_stations_feature(config):
//    global my_stations_enabled
//    my_stations_enabled = my_stations.set_config(config)


//def get_directories_page(subdir, directories, request):
//    page = vtuner.Page()
//    if len(directories) == 0:
//        page.add(vtuner.Display("No entries found"))
//        page.set_count(1)
//        return page
//    for directory in get_paged_elements(directories, request.args):
//        vtuner_directory = vtuner.Directory(directory.displayname,
//                                            url_for(subdir, _external = True, directory = directory.name),
//                                            directory.item_count)
//        page.add(vtuner_directory)
//    page.set_count(len(directories))
//    return page


//def get_stations_page(stations, request):
//    page = vtuner.Page()
//    if len(stations) == 0:
//        page.add(vtuner.Display("No stations found"))
//        page.set_count(1)
//        return page
//    for station in get_paged_elements(stations, request.args):
//        vtuner_station = station.to_vtuner()
//        if station_tracking:
//            vtuner_station.set_trackurl(request.host_url + PATH_ROOT + '/' + PATH_PLAY + '?id=' + vtuner_station.uid)
//        vtuner_station.icon = request.host_url + PATH_ROOT + '/' + PATH_ICON + '?id=' + vtuner_station.uid
//        page.add(vtuner_station)
//    page.set_count(len(stations))
//    return page


//def get_paged_elements(items, requestargs):
//    if requestargs.get('startitems'):
//        offset = int(requestargs.get('startitems')) - 1
//    elif requestargs.get('startItems'):
//        offset = int(requestargs.get('startItems')) - 1
//    elif requestargs.get('start'):
//        offset = int(requestargs.get('start')) - 1
//    else:
//        offset = 0
//    if offset > len(items):
//        logging.warning("Paging offset larger than item count")
//        return []
//    if requestargs.get('enditems'):
//        limit = int(requestargs.get('enditems'))
//    elif requestargs.get('endItems'):
//        limit = int(requestargs.get('endItems'))
//    elif requestargs.get('start') and requestargs.get('howmany'):
//        limit = int(requestargs.get('start')) - 1 + int(requestargs.get('howmany'))
//    else:
//        limit = len(items)
//    if limit < offset:
//        logging.warning("Paging limit smaller than offset")
//        return []
//    if limit > len(items):
//        limit = len(items)
//    return items[offset: limit]


//def get_station_by_id(stationid, additional_info= False):
//    station_id_prefix = generic.get_stationid_prefix(stationid)
//    if station_id_prefix == my_stations.ID_PREFIX:
//        return my_stations.get_station_by_id(generic.get_stationid_without_prefix(stationid))
//    elif station_id_prefix == radiobrowser.ID_PREFIX:
//        station = radiobrowser.get_station_by_id(generic.get_stationid_without_prefix(stationid))
//        if additional_info:
//            station.get_playable_url()
//        return station
//    return None


//def vtuner_redirect(url):
//    if request and request.host and not re.search("^[A-Za-z0-9]+\.vtuner\.com$", request.host):
//        logging.warning("You are not accessing a YCast redirect with a whitelisted host url (*.vtuner.com). "
//                        "Some AVRs have problems with this. The requested host was: %s", request.host)
//    return redirect(url, code = 302)


//@app.route('/setupapp/<path:path>',
//           methods = ['GET', 'POST'])
//def upstream(path):
//    if request.args.get('token') == '0':
//        return vtuner.get_init_token()
//    if request.args.get('search'):
//        return station_search()
//    if 'statxml.asp' in path and request.args.get('id'):
//        return get_station_info()
//    if 'navXML.asp' in path:
//return radiobrowser_landing()
//    if 'FavXML.asp' in path:
//return my_stations_landing()
//    if 'loginXML.asp' in path:
//return landing()
//    logging.error("Unhandled upstream query (/setupapp/%s)", path)
//    abort(404)


//@app.route('/',
//           defaults ={ 'path': ''},
//           methods = ['GET', 'POST'])
//@app.route('/' + PATH_ROOT + '/',
//           defaults ={ 'path': ''},
//           methods = ['GET', 'POST'])
//def landing(path= ''):
//    page = vtuner.Page()
//    page.add(vtuner.Directory('Radiobrowser', url_for('radiobrowser_landing', _external = True), 4))
//    if my_stations_enabled:
//        page.add(vtuner.Directory('My Stations', url_for('my_stations_landing', _external = True),
//                                  len(my_stations.get_category_directories())))
//    else:
//        page.add(vtuner.Display("'My Stations' feature not configured."))
//        page.set_count(1)
//    return page.to_string()


//@app.route('/' + PATH_ROOT + '/' + PATH_MY_STATIONS + '/',
//           methods = ['GET', 'POST'])
//def my_stations_landing():
//    directories = my_stations.get_category_directories()
//    return get_directories_page('my_stations_category', directories, request).to_string()


//@app.route('/' + PATH_ROOT + '/' + PATH_MY_STATIONS + '/<directory>',
//           methods = ['GET', 'POST'])
//def my_stations_category(directory):
//    stations = my_stations.get_stations_by_category(directory)
//    return get_stations_page(stations, request).to_string()


//@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/',
//           methods = ['GET', 'POST'])
//def radiobrowser_landing():
//    page = vtuner.Page()
//    page.add(vtuner.Directory('Genres', url_for('radiobrowser_genres', _external = True),
//                              len(radiobrowser.get_genre_directories())))
//    page.add(vtuner.Directory('Countries', url_for('radiobrowser_countries', _external = True),
//                              len(radiobrowser.get_country_directories())))
//    page.add(vtuner.Directory('Languages', url_for('radiobrowser_languages', _external = True),
//                              len(radiobrowser.get_language_directories())))
//    page.add(vtuner.Directory('Most Popular', url_for('radiobrowser_popular', _external = True),
//                              len(radiobrowser.get_stations_by_votes())))
//    page.set_count(4)
//    return page.to_string()


//@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/' + PATH_RADIOBROWSER_COUNTRY + '/',
//           methods = ['GET', 'POST'])
//def radiobrowser_countries():
//    directories = radiobrowser.get_country_directories()
//    return get_directories_page('radiobrowser_country_stations', directories, request).to_string()


//@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/' + PATH_RADIOBROWSER_COUNTRY + '/<directory>',
//           methods = ['GET', 'POST'])
//def radiobrowser_country_stations(directory):
//    stations = radiobrowser.get_stations_by_country(directory)
//    return get_stations_page(stations, request).to_string()


//@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/' + PATH_RADIOBROWSER_LANGUAGE + '/',
//           methods = ['GET', 'POST'])
//def radiobrowser_languages():
//    directories = radiobrowser.get_language_directories()
//    return get_directories_page('radiobrowser_language_stations', directories, request).to_string()


//@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/' + PATH_RADIOBROWSER_LANGUAGE + '/<directory>',
//           methods = ['GET', 'POST'])
//def radiobrowser_language_stations(directory):
//    stations = radiobrowser.get_stations_by_language(directory)
//    return get_stations_page(stations, request).to_string()


//@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/' + PATH_RADIOBROWSER_GENRE + '/',
//           methods = ['GET', 'POST'])
//def radiobrowser_genres():
//    directories = radiobrowser.get_genre_directories()
//    return get_directories_page('radiobrowser_genre_stations', directories, request).to_string()


//@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/' + PATH_RADIOBROWSER_GENRE + '/<directory>',
//           methods = ['GET', 'POST'])
//def radiobrowser_genre_stations(directory):
//    stations = radiobrowser.get_stations_by_genre(directory)
//    return get_stations_page(stations, request).to_string()


//@app.route('/' + PATH_ROOT + '/' + PATH_RADIOBROWSER + '/' + PATH_RADIOBROWSER_POPULAR + '/',
//           methods = ['GET', 'POST'])
//def radiobrowser_popular():
//    stations = radiobrowser.get_stations_by_votes()
//    return get_stations_page(stations, request).to_string()


//@app.route('/' + PATH_ROOT + '/' + PATH_SEARCH + '/',
//           methods = ['GET', 'POST'])
//def station_search():
//    query = request.args.get('search')
//    if not query or len(query) < 3:
//        page = vtuner.Page()
//        page.add(vtuner.Display("Search query too short"))
//        page.set_count(1)
//        return page.to_string()
//    else:
//        # TODO: we also need to include 'my station' elements
//        stations = radiobrowser.search(query)
//        return get_stations_page(stations, request).to_string()


//@app.route('/' + PATH_ROOT + '/' + PATH_PLAY,
//           methods = ['GET', 'POST'])
//def get_stream_url():
//    stationid = request.args.get('id')
//    if not stationid:
//    logging.error("Stream URL without station ID requested")
//        abort(400)
//    station = get_station_by_id(stationid, additional_info = True)
//    if not station:
//    logging.error("Could not get station with id '%s'", stationid)
//        abort(404)
//    logging.debug("Station with ID '%s' requested", station.id)
//    return vtuner_redirect(station.url)


//@app.route('/' + PATH_ROOT + '/' + PATH_STATION,
//           methods = ['GET', 'POST'])
//def get_station_info():
//    stationid = request.args.get('id')
//    if not stationid:
//    logging.error("Station info without station ID requested")
//        abort(400)
//    station = get_station_by_id(stationid, additional_info = (not station_tracking))
//    if not station:
//    logging.error("Could not get station with id '%s'", stationid)
//        page = vtuner.Page()
//        page.add(vtuner.Display("Station not found"))
//        page.set_count(1)
//        return page.to_string()
//    vtuner_station = station.to_vtuner()
//    if station_tracking:
//        vtuner_station.set_trackurl(request.host_url + PATH_ROOT + '/' + PATH_PLAY + '?id=' + vtuner_station.uid)
//    vtuner_station.icon = request.host_url + PATH_ROOT + '/' + PATH_ICON + '?id=' + vtuner_station.uid
//    page = vtuner.Page()
//    page.add(vtuner_station)
//    page.set_count(1)
//    return page.to_string()


//@app.route('/' + PATH_ROOT + '/' + PATH_ICON,
//           methods = ['GET', 'POST'])
//def get_station_icon():
//    stationid = request.args.get('id')
//    if not stationid:
//    logging.error("Station icon without station ID requested")
//        abort(400)
//    station = get_station_by_id(stationid)
//    if not station:
//    logging.error("Could not get station with id '%s'", stationid)
//        abort(404)
//    if not hasattr(station, 'icon') or not station.icon:
//        logging.warning("No icon information found for station with id '%s'", stationid)
//        abort(404)
//    station_icon = station_icons.get_icon(station)
//    if not station_icon:
//    logging.error("Could not get station icon for station with id '%s'", stationid)
//        abort(404)
//    response = make_response(station_icon)
//    response.headers.set('Content-Type', 'image/jpeg')
//    return response