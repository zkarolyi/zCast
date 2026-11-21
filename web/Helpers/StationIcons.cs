using Browser;
using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace Helpers
{
    public class IconFetcher
    {
        private readonly ILogger<IconFetcher> logger;
        private readonly IMemoryCache memoryCache;
        private readonly HttpClient httpClient;

        public const int MAX_SIZE = 290;
        public const string CACHE_NAME = "icons_{0}";

        public IconFetcher(ILogger<IconFetcher> logger, IHttpClientFactory httpClientFactory, IMemoryCache memoryCache)
        {
            this.logger = logger;
            this.memoryCache = memoryCache;
            this.httpClient = httpClientFactory.CreateClient(RadioBrowser.CLIENT_NAME);
        }

        public async Task<byte[]> GetIconAsync(VTuner.Station station)
        {
            var cacheKey = string.Format(CACHE_NAME, station.Uid);
            var cachedValue = await (memoryCache.GetOrCreate(cacheKey, async entry =>
                {
                    entry.SlidingExpiration = TimeSpan.FromHours(1);
                    logger.LogWarning($"Station icon cache miss. Fetching and converting station icon for station id '{station.Uid}'");
                    byte[] ret;

                    try
                    {
                        var response = await httpClient.GetByteArrayAsync(station.Icon);
                        if (response == null || response.Length == 0)
                        {
                            logger.LogWarning($"Could not get station icon data from {station.Icon}");
                            return Array.Empty<byte>();
                        }

                        // SkiaSharp is cross-platform (System.Drawing only works on Windows).
                        ret = ConvertToJpeg(response, MAX_SIZE);
                        if (ret.Length == 0)
                        {
                            logger.LogWarning($"Station icon from {station.Icon} is not a decodable image");
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        logger.LogWarning($"Connection to station icon URL failed: {ex.Message}");
                        return Array.Empty<byte>();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning($"Station icon conversion error: {ex.Message}");
                        return Array.Empty<byte>();
                    }

                    return ret;
                }) ?? Task.FromResult(Array.Empty<byte>()));

            // Don't keep failed fetches in the cache for an hour; retry on the next request.
            if (cachedValue == null || cachedValue.Length == 0)
            {
                memoryCache.Remove(cacheKey);
                return Array.Empty<byte>();
            }

            return cachedValue;
        }

        /// <summary>
        /// Scales the image so its longer side is <paramref name="maxSize"/> (keeping aspect ratio),
        /// flattens transparency onto white (JPEG has no alpha) and encodes it as JPEG.
        /// Returns an empty array if the data is not a decodable image.
        /// </summary>
        public static byte[] ConvertToJpeg(byte[] imageData, int maxSize)
        {
            using var source = SKBitmap.Decode(imageData);
            if (source == null || source.Width <= 0 || source.Height <= 0)
            {
                return Array.Empty<byte>();
            }

            var ratio = maxSize / (float)Math.Max(source.Width, source.Height);
            var width = Math.Max(1, (int)(source.Width * ratio));
            var height = Math.Max(1, (int)(source.Height * ratio));

            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            using (var sourceImage = SKImage.FromBitmap(source))
            {
                canvas.DrawImage(sourceImage, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
            }

            using var snapshot = surface.Snapshot();
            using var jpeg = snapshot.Encode(SKEncodedImageFormat.Jpeg, 90);
            return jpeg?.ToArray() ?? Array.Empty<byte>();
        }
    }
}

//import logging
//import requests
//import io
//import os

//from PIL import Image

//import ycast.generic as generic
//from ycast import __version__

//MAX_SIZE = 290
//CACHE_NAME = 'icons'


//def get_icon(station):
//    cache_path = generic.get_cache_path(CACHE_NAME)
//    if not cache_path:
//    return None
//    station_icon_file = cache_path + '/' + station.id
//    if not os.path.exists(station_icon_file):
//        logging.debug("Station icon cache miss. Fetching and converting station icon for station id '%s'", station.id)
//        headers = { 'User-Agent': generic.USER_AGENT + '/' + __version__}
//try:
//            response = requests.get(station.icon, headers = headers)
//        except requests.exceptions.ConnectionError as err:
//            logging.error("Connection to station icon URL failed (%s)", err)
//            return None
//        if response.status_code != 200:
//            logging.error("Could not get station icon data from %s (HTML status %s)", station.icon, response.status_code)
//            return None
//        try:
//            image = Image.open(io.BytesIO(response.content))
//            image = image.convert("RGB")
//            if image.size[0] > image.size[1]:
//                ratio = MAX_SIZE / image.size[0]
//            else:
//                ratio = MAX_SIZE / image.size[1]
//            image = image.resize((int(image.size[0] * ratio), int(image.size[1] * ratio)), Image.ANTIALIAS)
//            image.save(station_icon_file, format = "JPEG")
//        except Exception as e:
//            logging.error("Station icon conversion error (%s)", e)
//            return None
//    try:
//        with open(station_icon_file, 'rb') as file:
//            image_conv = file.read()
//    except PermissionError:
//        logging.error("Could not access station icon file in cache (%s) because of access permissions",
//                      station_icon_file)
//        return None
//    return image_conv