using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SniffApi.Models;
using System.Diagnostics;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.DAO.Data;
using SoundFingerprinting.Emy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using System.IO;

namespace SniffApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SongFingerprintingController : ControllerBase
    {

        private static readonly EmyModelService emyModelService = EmyModelService.NewInstance("localhost", 3399); // connect to Emy on port 3399
        private static readonly IAudioService audioService = new SoundFingerprintingAudioService(); // default audio library

        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private readonly ILogger<SongFingerprintingController> _logger;

        private IWebHostEnvironment _hostingEnvironment;

        public SongFingerprintingController(ILogger<SongFingerprintingController> logger, IWebHostEnvironment environment)
        {
            _logger = logger;
            _hostingEnvironment = environment;
        }

        [HttpGet]
        public async Task<IEnumerable<Song>> Get()
        {

            Song song = await FetchSong();
            return Enumerable.Range(1, 1).Select(index => song)
            .ToArray();

            // var rng = new Random();
            // return Enumerable.Range(1, 5).Select(index => new Song
            // {
            //     Id = "10",
            //     Artist = "toto",
            //     Title = "Test title"
            // })
            // .ToArray();
        }

        async Task<Song> FetchSong()
        {
            Console.WriteLine("Find with Emy");
            Console.WriteLine("The current time is " + DateTime.Now);

            try
            {

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                string ffpmSOng = "song.wav";
                TrackData foundTrack = await GetBestMatchForSongEmy(ffpmSOng);
                Console.WriteLine("Found track : " + foundTrack.Title + " by " + foundTrack.Artist);
                stopwatch.Stop();
                TimeSpan stopwatchElapsed = stopwatch.Elapsed;
                Console.WriteLine("GetBestMatchForSongEmy " + ffpmSOng + " in " + Convert.ToInt32(stopwatchElapsed.TotalMilliseconds) + "ms.");

                return new Song
                {
                    Id = foundTrack.Id,
                    Artist = foundTrack.Artist,
                    Title = foundTrack.Title
                };
            }
            catch (System.Exception)
            {
                throw new Exception("Emy is down!");
            }

        }

        async Task<TrackData> GetBestMatchForSongEmy(string queryAudioFile)
        {
            int secondsToAnalyze = 10; // number of seconds to analyze from query file
            int startAtSecond = 0; // start at the begining            

            // query Emy database
            var queryResult = await QueryCommandBuilder.Instance.BuildQueryCommand()
                                                    .From(queryAudioFile, secondsToAnalyze, startAtSecond)
                                                    .UsingServices(emyModelService, audioService)
                                                    .Query();

            // register matches s.t. they appear in the dashboard					
            emyModelService.RegisterMatches(queryResult.ResultEntries);

            if (queryResult.BestMatch != null)
            {
                return queryResult.BestMatch.Track;
            }
            else throw new Exception("Track not found from: " + queryAudioFile);
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IList<IFormFile> files)
        {
            TrackData foundTrack = null;

            string uploads = Path.Combine(_hostingEnvironment.WebRootPath, "uploads");
            foreach (IFormFile file in files)
            {
                if (file.Length > 0)
                {
                    string filePath = Path.Combine(uploads, file.FileName);
                    using (Stream fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(fileStream);

                        fileStream.Close();
                    }

                    try
                    {
                        foundTrack = await GetBestMatchForSongEmy(filePath);
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Track not found from: " + filePath);
                    }
                    finally
                    {
                        System.IO.File.Delete(filePath);
                    }

                }
            }

            return foundTrack != null ? Ok(foundTrack) : Ok("Track not found");
        }
    }
}
