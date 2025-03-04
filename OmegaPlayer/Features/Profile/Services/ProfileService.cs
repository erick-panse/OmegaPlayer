﻿using OmegaPlayer.Features.Profile.Models;
using OmegaPlayer.Infrastructure.Data.Repositories.Profile;
using OmegaPlayer.Features.Library.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using OmegaPlayer.Features.Library.Models;
using OmegaPlayer.Infrastructure.Services.Images;

namespace OmegaPlayer.Features.Profile.Services
{
    public class ProfileService
    {
        private readonly ProfileRepository _profileRepository;
        private readonly StandardImageService _standardImageService;
        private readonly MediaService _mediaService;
        private const string PROFILE_PHOTO_DIR = "profile_photo";

        public ProfileService(
            ProfileRepository profileRepository,
            StandardImageService standardImageService,
            MediaService mediaService)
        {
            _profileRepository = profileRepository;
            _standardImageService = standardImageService;
            _mediaService = mediaService;
        }

        public async Task<Profiles> GetProfileById(int profileID)
        {
            return await _profileRepository.GetProfileById(profileID);
        }

        public async Task<List<Profiles>> GetAllProfiles()
        {
            return await _profileRepository.GetAllProfiles();
        }

        public async Task<int> AddProfile(Profiles profile, Stream photoStream = null)
        {
            if (photoStream != null)
            {
                var media = new Media { MediaType = PROFILE_PHOTO_DIR };
                var mediaId = await _mediaService.AddMedia(media);

                var photoPath = await SaveProfilePhoto(photoStream, mediaId);
                await _mediaService.UpdateMediaFilePath(mediaId, photoPath);
                profile.PhotoID = mediaId;
            }

            return await _profileRepository.AddProfile(profile);
        }

        public async Task UpdateProfile(Profiles profile, Stream photoStream = null)
        {
            if (photoStream != null)
            {
                if (profile.PhotoID > 0)
                {
                    var oldMedia = await _mediaService.GetMediaById(profile.PhotoID);
                    if (File.Exists(oldMedia?.CoverPath))
                    {
                        File.Delete(oldMedia.CoverPath);
                    }
                }

                var media = new Media { MediaType = PROFILE_PHOTO_DIR };
                var mediaId = await _mediaService.AddMedia(media);

                var photoPath = await SaveProfilePhoto(photoStream, mediaId);
                await _mediaService.UpdateMediaFilePath(mediaId, photoPath);
                profile.PhotoID = mediaId;
            }

            await _profileRepository.UpdateProfile(profile);
        }

        public async Task DeleteProfile(int profileID)
        {
            var profile = await GetProfileById(profileID);
            if (profile.PhotoID > 0)
            {
                var media = await _mediaService.GetMediaById(profile.PhotoID);
                if (File.Exists(media?.CoverPath))
                {
                    File.Delete(media.CoverPath);
                }
                await _mediaService.DeleteMedia(profile.PhotoID);
            }
            await _profileRepository.DeleteProfile(profileID);
        }

        private async Task<string> SaveProfilePhoto(Stream photoStream, int mediaId)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var photoDir = Path.Combine(baseDir, "media", PROFILE_PHOTO_DIR, mediaId.ToString("D7"));
            Directory.CreateDirectory(photoDir);

            var filePath = Path.Combine(photoDir, $"profile_{mediaId.ToString("D7")}_photo.jpg");

            using (var fileStream = File.Create(filePath))
            {
                await photoStream.CopyToAsync(fileStream);
            }

            return filePath;
        }

        public async Task<Bitmap> LoadProfilePhoto(int photoId)
        {
            if (photoId <= 0) return null;

            var media = await _mediaService.GetMediaById(photoId);
            if (media == null || !File.Exists(media.CoverPath)) return null;

            try
            {
                return await _standardImageService.LoadLowQualityAsync(media.CoverPath);
            }
            catch
            {
                return null;
            }
        }

        public async Task<Bitmap> LoadMediumQualityProfilePhoto(int photoId)
        {
            if (photoId <= 0) return null;

            var media = await _mediaService.GetMediaById(photoId);
            if (media == null || !File.Exists(media.CoverPath)) return null;

            try
            {
                return await _standardImageService.LoadMediumQualityAsync(media.CoverPath);
            }
            catch
            {
                return null;
            }
        }

        public async Task<Bitmap> LoadHighQualityProfilePhoto(int photoId)
        {
            if (photoId <= 0) return null;

            var media = await _mediaService.GetMediaById(photoId);
            if (media == null || !File.Exists(media.CoverPath)) return null;

            try
            {
                return await _standardImageService.LoadHighQualityAsync(media.CoverPath);
            }
            catch
            {
                return null;
            }
        }

    }
}