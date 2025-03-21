﻿using Genres = OmegaPlayer.Features.Library.Models.Genres;
using File = System.IO.File;
using System.Linq;
using System;
using TagLib;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ComponentModel;
using OmegaPlayer.Features.Library.Models;
using Avalonia.Media.Imaging;
using OmegaPlayer.Infrastructure.Services.Cache;

namespace OmegaPlayer.Features.Library.Services
{
    public class TrackMetadataService
    {
        private readonly TracksService _trackService;
        private readonly ArtistsService _artistService;
        private readonly AlbumService _albumService;
        private readonly GenresService _genreService;
        private readonly MediaService _mediaService;
        private readonly TrackArtistService _trackArtistService;
        private readonly TrackGenreService _trackGenreService;

        public TrackMetadataService(
            TracksService trackService,
            ArtistsService artistService,
            AlbumService albumService,
            GenresService genreService,
            MediaService mediaService,
            TrackArtistService trackArtistService,
            TrackGenreService trackGenreService)
        {
            _trackService = trackService;
            _artistService = artistService;
            _albumService = albumService;
            _genreService = genreService;
            _mediaService = mediaService;
            _trackArtistService = trackArtistService;
            _trackGenreService = trackGenreService;
        }

        public async Task PopulateTrackMetadata(string filePath)
        {
            //Populate every data used by the player and that doesn't already exists into the DB
            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException("The specified track file does not exist.");
                }

                var file = TagLib.File.Create(filePath);

                //Check if track already exists and Fetch it or creates Track obj to populate later on
                Tracks track = await _trackService.GetTrackByPath(filePath) ?? new Tracks();

                // Handle Artist
                var fileArtistNames = file.Tag.Performers;
                Artists artist = new Artists();
                var artistsIds = new List<int>();
                foreach (var artistName in fileArtistNames)// register all artists who are listed in the file
                {
                    artist = await _artistService.GetArtistByName(artistName) ?? new Artists();

                    if (artistName.Length > 0 && artist.ArtistID == 0)
                    {
                        artist.ArtistName = artistName;
                        artist.Bio = "";
                        artist.CreatedAt = DateTime.Now;
                        artist.UpdatedAt = DateTime.Now;

                        artist.ArtistID = await _artistService.AddArtist(artist); //Insert the data into the DB and generates an ID
                        artistsIds.Add(artist.ArtistID);
                    }
                }

                // Handle Media to Get CoverID Then Album
                var albumTag = file.Tag.Album;
                Albums album = await _albumService.GetAlbumByTitle(albumTag, artistsIds.First()) ?? new Albums();//using first artist of the track

                // Handle Media (Album cover, etc.)
                if (track.CoverID == 0)
                {
                    var media = await SaveMedia(file, filePath, "track_cover");
                    track.CoverID = media.MediaID;
                    album.CoverID = media.MediaID;
                }

                // Handle Album
                if (!string.IsNullOrEmpty(albumTag) && album.AlbumID == 0)
                {
                    album.Title = file.Tag.Album;
                    //album.ReleaseDate = file.Tag.Year > 0 ? new DateTime((int)file.Tag.Year, 1, 1) : (DateTime?)null;
                    album.CreatedAt = DateTime.Now;
                    album.UpdatedAt = DateTime.Now;
                    album.ArtistID = artist != null ? artistsIds.First() : 0; //Insert artistId of the first artist and if there isn't one insert 0 (AKA unknown artist)
                    album.AlbumID = await _albumService.AddAlbum(album); //Insert the data into the DB and generates an ID
                }

                if (album.CoverID == 0)
                {
                    var media = await SaveMedia(file, filePath, "album_cover");
                    album.CoverID = media.MediaID;
                }


                // Handle Genre
                var genreName = file.Tag.Genres.First();
                Genres genre = await _genreService.GetGenreByName(genreName) ?? new Genres();

                if (file.Tag.Genres.Length > 0 && genre.GenreID == 0)
                {
                    genre.GenreName = genreName;

                    genre.GenreID = await _genreService.AddGenre(genre);
                }


                // Handle Track after creating / finding the albumID
                if (track.TrackID == 0)
                {
                    track.FilePath = filePath;
                    track.Title = string.IsNullOrEmpty(file.Tag.Title) ? Path.GetFileNameWithoutExtension(filePath) : file.Tag.Title;
                    track.Duration = file.Properties.Duration;
                    track.BitRate = file.Properties.AudioBitrate;
                    track.FileSize = (int)new FileInfo(filePath).Length;
                    track.FileType = Path.GetExtension(filePath)?.TrimStart('.');
                    track.CreatedAt = DateTime.Now;
                    track.UpdatedAt = DateTime.Now;
                    track.PlayCount = 0;// Initialize play count to 0

                    track.AlbumID = album != null ? album.AlbumID : 0; // adds AlbumID to the track
                    track.GenreID = genre != null ? genre.GenreID : 0;// adds GenreID to the track
                    track.TrackID = await _trackService.AddTrack(track); //Insert the data into the DB and generates an ID
                }

                // Associate Track with Artist
                var trackID = track.TrackID;
                var genreID = genre.GenreID;
                var trackArtist = new TrackArtist();

                foreach (var artistId in artistsIds)
                {
                    trackArtist = await _trackArtistService.GetTrackArtist(trackID, artistId) ?? new TrackArtist(); //search association to confirm it doesn't exist
                    if (artistId != 0 && trackArtist.ArtistID == 0 && trackArtist.TrackID == 0) //if there is no existing association create a new one
                    {
                        trackArtist.TrackID = track.TrackID;
                        trackArtist.ArtistID = artistId;

                        await _trackArtistService.AddTrackArtist(trackArtist);
                    }
                }

                // Associate Track with Genre
                var trackGenre = await _trackGenreService.GetTrackGenre(trackID, genreID) ?? new TrackGenre();
                if (genre.GenreID != 0 && trackGenre.GenreID == 0 && trackGenre.TrackID == 0) //assuming every track has only one genre
                {
                    trackGenre.TrackID = track.TrackID;
                    trackGenre.GenreID = genre.GenreID;

                    await _trackGenreService.AddTrackGenre(trackGenre);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while populating track metadata: {ex.Message}");
                throw;
            }
        }
        public async Task<Media> SaveMedia(TagLib.File file, string filePath, string mediaType)
        {
            Media media = null;
            if (file.Tag.Pictures.Length > 0)
            {
                var picture = file.Tag.Pictures.First();

                // Step 1: Insert media without the file path and retrieve the MediaID
                media = new Media
                {
                    CoverPath = null, // Will be set later after saving the image
                    MediaType = mediaType
                };

                // Insert into the database and get MediaID
                int mediaId = await _mediaService.AddMedia(media);
                media.MediaID = mediaId;

                // Step 2: Save the image with MediaID
                string baseDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "media", mediaType);
                string subDirectory = Path.Combine(baseDirectory, mediaId.ToString("D7")); // Ensures 0000001 format

                // Create all necessary directories
                Directory.CreateDirectory(subDirectory);

                // Step 3: Save the image with MediaID and update the file path
                using (var ms = new MemoryStream(picture.Data.Data))
                {
                    var imageFilePath = await SaveImage(ms, mediaType, mediaId);
                    media.CoverPath = imageFilePath;
                }

                // Step 4: Update the Media record with the correct file path
                await _mediaService.UpdateMediaFilePath(mediaId, media.CoverPath);
            }
            return media;
        }

        public async Task<string> SaveImage(Stream imageStream, string mediaType, int mediaID)
        {
            var projectBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var baseDirectory = Path.Combine(projectBaseDirectory, "media", mediaType);
            var subDirectory = Path.Combine(baseDirectory, mediaID.ToString("D7"));

            Directory.CreateDirectory(subDirectory);

            string fileName = mediaType switch
            {
                "track_cover" => $"track_{mediaID.ToString("D7")}_cover.jpg",
                "album_cover" => $"album_{mediaID.ToString("D7")}_cover.jpg",
                "artist_photo" => $"artist_{mediaID.ToString("D7")}_photo.jpg",
                _ => throw new ArgumentException("Invalid media type")
            };

            var imagePath = Path.Combine(subDirectory, fileName);

            using (var fileStream = new FileStream(imagePath, FileMode.Create, FileAccess.Write))
            {
                await imageStream.CopyToAsync(fileStream);
            }

            return imagePath;
        }

        public async Task UpdateTrackMetada(string filePath)
        {
            //TODO
        }
    }
}
