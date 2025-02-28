﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OmegaPlayer.Features.Library.Models;
using OmegaPlayer.Infrastructure.Services.Cache;
using OmegaPlayer.Infrastructure.Data.Repositories.Library;
using OmegaPlayer.Infrastructure.Data.Repositories;
using System.Linq;

namespace OmegaPlayer.Features.Library.Services
{
    public class AlbumDisplayService
    {
        private readonly AlbumRepository _albumRepository;
        private readonly ImageCacheService _imageCacheService;
        private readonly MediaService _mediaService;
        private readonly AllTracksRepository _allTracksRepository;
        private readonly ArtistsService _artistService;

        public AlbumDisplayService(
            AlbumRepository albumRepository,
            ImageCacheService imageCacheService,
            MediaService mediaService,
            AllTracksRepository allTracksRepository,
            ArtistsService artistService)
        {
            _albumRepository = albumRepository;
            _imageCacheService = imageCacheService;
            _mediaService = mediaService;
            _allTracksRepository = allTracksRepository;
            _artistService = artistService;
        }

        public async Task<List<AlbumDisplayModel>> GetAllAlbumsAsync()
        {
            var albums = _allTracksRepository.AllAlbums; 

            // If no albums left, return empty list
            if (!albums.Any())
            {
                return new List<AlbumDisplayModel>();
            }

            var displayModels = new List<AlbumDisplayModel>();

            foreach (var album in albums)
            {
                var displayModel = new AlbumDisplayModel
                {
                    AlbumID = album.AlbumID,
                    Title = album.Title,
                    ArtistID = album.ArtistID,
                    ReleaseDate = album.ReleaseDate
                };

                // Get tracks for this album
                var tracks = await GetAlbumTracksAsync(album.AlbumID);
                displayModel.TrackIDs = tracks.Select(t => t.TrackID).ToList();
                displayModel.TotalDuration = TimeSpan.FromTicks(tracks.Sum(t => t.Duration.Ticks));

                // Get artist name
                var artist = await _artistService.GetArtistById(album.ArtistID);
                if (artist != null)
                {
                    displayModel.ArtistName = artist.ArtistName;
                }

                var media = await _mediaService.GetMediaById(album.CoverID);
                if (media != null)
                {
                    displayModel.CoverPath = media.CoverPath;
                }

                displayModels.Add(displayModel);
            }

            return displayModels;
        }

        public async Task<List<TrackDisplayModel>> GetAlbumTracksAsync(int albumId)
        {
            // Get all tracks that belong to this album from AllTracksRepository
            return _allTracksRepository.AllTracks
                .Where(t => t.AlbumID == albumId)
                .ToList();
        }

        public async Task LoadAlbumCoverAsync(AlbumDisplayModel album)
        {
            if (!string.IsNullOrEmpty(album.CoverPath))
            {
                try
                {
                    album.Cover = await _imageCacheService.LoadThumbnailAsync(album.CoverPath, 110, 110);
                    album.CoverSize = "low";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading album cover: {ex.Message}");
                    // Handle error - maybe set a default cover
                }
            }
        }

        public async Task<AlbumDisplayModel> GetAlbumByIdAsync(int albumId)
        {
            var album = await _albumRepository.GetAlbumById(albumId);
            if (album == null) return null;

            var displayModel = new AlbumDisplayModel
            {
                AlbumID = album.AlbumID,
                Title = album.Title,
                ArtistID = album.ArtistID,
                ReleaseDate = album.ReleaseDate
            };

            // Get tracks for this album
            var tracks = await GetAlbumTracksAsync(album.AlbumID);
            displayModel.TrackIDs = tracks.Select(t => t.TrackID).ToList();
            displayModel.TotalDuration = TimeSpan.FromTicks(tracks.Sum(t => t.Duration.Ticks));

            // Get artist name
            var artist = await _artistService.GetArtistById(album.ArtistID);
            if (artist != null)
            {
                displayModel.ArtistName = artist.ArtistName;
            }

            var media = await _mediaService.GetMediaById(album.CoverID);
            if (media != null)
            {
                displayModel.CoverPath = media.CoverPath;
                await LoadAlbumCoverAsync(displayModel);
            }

            return displayModel;
        }

        public async Task LoadHighResAlbumCoverAsync(AlbumDisplayModel album)
        {
            if (!string.IsNullOrEmpty(album.CoverPath))
            {
                try
                {
                    album.Cover = await _imageCacheService.LoadThumbnailAsync(album.CoverPath, 160, 160);
                    album.CoverSize = "high";
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading high-res album cover: {ex.Message}");
                }
            }
        }

    }
}
