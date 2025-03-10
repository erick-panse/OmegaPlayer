﻿using System.Collections.Generic;
using System;
using Avalonia.Media.Imaging;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using OmegaPlayer.Features.Playback.Models;
using OmegaPlayer.Infrastructure.Services.Images;
using OmegaPlayer.Infrastructure.Data.Repositories;
using Avalonia;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia;
using Avalonia.Media;
using OmegaPlayer.Features.Library.Models;

namespace OmegaPlayer.Features.Library.Services
{

    public class TrackLikeUpdateMessage
    {
        public int TrackId { get; }
        public bool IsLiked { get; }

        public TrackLikeUpdateMessage(int trackId, bool isLiked)
        {
            TrackId = trackId;
            IsLiked = isLiked;
        }
    }

    public class TrackDisplayService
    {
        private readonly StandardImageService _standardImageService;
        private readonly AllTracksRepository _allTracksRepository;
        private readonly Bitmap _defaultCover;
        private readonly TrackMetadataService _trackMetadataService;
        private readonly MediaService _mediaService;

        public List<TrackDisplayModel> AllTracks { get; set; }

        public TrackDisplayService(
            StandardImageService standardImageService,
            AllTracksRepository allTracksRepository,
            TrackMetadataService trackMetadataService,
            MediaService mediaService)
        {
            _standardImageService = standardImageService;
            _allTracksRepository = allTracksRepository;
            _trackMetadataService = trackMetadataService;
            _mediaService = mediaService;

            _defaultCover = LoadDefaultCover();
        }

        private Bitmap LoadDefaultCover()
        {
            try
            {
                string defaultImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "Resources", "Assets", "default-cover.jpg");

                if (File.Exists(defaultImagePath))
                {
                    using var stream = File.OpenRead(defaultImagePath);
                    return new Bitmap(stream);
                }
                return CreateDefaultBitmap();
            }
            catch (Exception ex)
            {
                ShowMessageBox($"Error loading default cover: {ex.Message}");
                return CreateDefaultBitmap();
            }
        }

        private Bitmap CreateDefaultBitmap()
        {
            var pixelSize = new PixelSize(StandardImageService.MEDIUM_QUALITY_SIZE, StandardImageService.MEDIUM_QUALITY_SIZE);
            var size = new Size(StandardImageService.MEDIUM_QUALITY_SIZE, StandardImageService.MEDIUM_QUALITY_SIZE);
            var dpi = new Vector(96, 96);

            var renderTargetBitmap = new RenderTargetBitmap(pixelSize, dpi);

            using (var drawingContext = renderTargetBitmap.CreateDrawingContext())
            {
                drawingContext.FillRectangle(
                    Brushes.Gray,
                    new Rect(size));
            }

            return renderTargetBitmap;
        }

        private async Task<Bitmap> ExtractAndSaveCover(TrackDisplayModel track)
        {
            try
            {
                Console.WriteLine($"\nAttempting to extract cover for track: {track.Title}");
                Console.WriteLine($"Track file path: {track.FilePath}");
                Console.WriteLine($"Track CoverID: {track.CoverID}"); // Log the CoverID

                var file = TagLib.File.Create(track.FilePath);

                if (file.Tag.Pictures.Length > 0)
                {
                    Console.WriteLine("Found embedded cover art in the file");

                    // IMPORTANT: Use the existing Media record instead of creating a new one
                    var media = new Media
                    {
                        MediaID = track.CoverID,  // Use the existing CoverID
                        MediaType = "track_cover",
                        CoverPath = null  // Will be updated after saving the image
                    };

                    // Save the image using the existing MediaID
                    using (var ms = new MemoryStream(file.Tag.Pictures.First().Data.Data))
                    {
                        var imageFilePath = await _trackMetadataService.SaveImage(ms, "track_cover", track.CoverID);
                        media.CoverPath = imageFilePath;

                        // Update the existing Media record with the new path
                        await _mediaService.UpdateMediaFilePath(track.CoverID, imageFilePath);

                        // Update the track's cover path
                        track.CoverPath = imageFilePath;

                        try
                        {
                            var thumbnail = await _standardImageService.LoadMediumQualityAsync(imageFilePath);
                            Console.WriteLine("Successfully loaded new thumbnail");
                            return thumbnail;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to load new thumbnail: {ex.Message}");
                        }
                    }
                }

                return _defaultCover;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cover extraction: {ex.Message}");
                return _defaultCover;
            }
        }


        public async Task LoadThumbnailAsync(TrackDisplayModel track)
        {
            try
            {
                if (string.IsNullOrEmpty(track.CoverPath) || !File.Exists(track.CoverPath))
                {
                    track.Thumbnail = await ExtractAndSaveCover(track);
                    return;
                }

                track.Thumbnail = await _standardImageService.LoadLowQualityAsync(track.CoverPath);
                track.ThumbnailSize = "low";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading thumbnail for track {track.Title}: {ex.Message}");
                track.Thumbnail = _defaultCover;
            }
        }

        public async Task LoadMediumQualityThumbnailAsync(TrackDisplayModel track)
        {
            try
            {
                if (string.IsNullOrEmpty(track.CoverPath) || !File.Exists(track.CoverPath))
                {
                    track.Thumbnail = await ExtractAndSaveCover(track);
                    return;
                }

                track.Thumbnail = await _standardImageService.LoadMediumQualityAsync(track.CoverPath);
                track.ThumbnailSize = "medium";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading medium quality thumbnail for track {track.Title}: {ex.Message}");
                track.Thumbnail = _defaultCover;
            }
        }

        public async Task LoadHighResThumbnailAsync(TrackDisplayModel track)
        {
            try
            {
                if (string.IsNullOrEmpty(track.CoverPath) || !File.Exists(track.CoverPath))
                {
                    ShowMessageBox($"High-res cover not found for track: {track.Title}. Attempting to extract from file...");
                    track.Thumbnail = await ExtractAndSaveCover(track);
                    return;
                }

                track.Thumbnail = await _standardImageService.LoadHighQualityAsync(track.CoverPath);
                track.ThumbnailSize = "high";
            }
            catch (Exception ex)
            {
                ShowMessageBox($"Error loading high-res thumbnail for track {track.Title}: {ex.Message}");
                track.Thumbnail = _defaultCover;
            }
        }

        public async Task<List<TrackDisplayModel>> GetTrackDisplaysFromQueue(List<QueueTracks> queueTracks)
        {
            // Retrieve all tracks for the profile from the repository

            if (_allTracksRepository.AllTracks.Count <= 0)
            {
                await _allTracksRepository.LoadTracks();
                if (_allTracksRepository.AllTracks == null) return null;
            }
            var allTracks = _allTracksRepository.AllTracks;
            // Create a dictionary for quick lookup of track IDs from the queueTracks
            var trackIdToOrderMap = queueTracks.ToDictionary(qt => qt.TrackID, qt => qt.TrackOrder);

            // Filter tracks from the repository based on trackIds present in the queueTracks
            var filteredTracks = allTracks
                .Where(track => trackIdToOrderMap.ContainsKey(track.TrackID)) // Only tracks present in queueTracks
                .ToList();

            // Sort the filtered tracks according to their TrackOrder in queueTracks
            var sortedTracks = filteredTracks
                .OrderBy(track => trackIdToOrderMap[track.TrackID]) // Sort by TrackOrder
                .ToList();

            // Return the sorted list of TrackDisplayModel
            return sortedTracks;
        }


        private async void ShowMessageBox(string message)
        {
            var messageBox = MessageBoxManager.GetMessageBoxStandard("DI Resolution Result", message, ButtonEnum.Ok, Icon.Info);
            await messageBox.ShowWindowAsync();
        }

    }
}