﻿using OmegaPlayer.Features.Library.Models;
using OmegaPlayer.Infrastructure.Data.Repositories.Library;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OmegaPlayer.Features.Library.Services
{
    public class MediaService
    {
        private readonly MediaRepository _mediaRepository;

        public MediaService(MediaRepository mediaRepository)
        {
            _mediaRepository = mediaRepository;
        }

        public async Task<Media> GetMediaById(int mediaID)
        {
            try
            {
                return await _mediaRepository.GetMediaById(mediaID);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching Media by ID: {ex.Message}");
                throw;
            }
        }

        public async Task<List<Media>> GetAllMedia()
        {
            try
            {
                return await _mediaRepository.GetAllMedia();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching all Media: {ex.Message}");
                throw;
            }
        }

        public async Task<int> AddMedia(Media media)
        {
            try
            {
                return await _mediaRepository.AddMedia(media);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding Media: {ex.Message}");
                throw;
            }
        }

        public async Task UpdateMedia(Media media)
        {
            try
            {
                await _mediaRepository.UpdateMedia(media);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Media: {ex.Message}");
                throw;
            }
        }
        public async Task UpdateMediaFilePath(int mediaID, string coverPath)
        {
            try
            {
                await _mediaRepository.UpdateMediaFilePath(mediaID, coverPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating Media: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteMedia(int mediaID)
        {
            try
            {
                await _mediaRepository.DeleteMedia(mediaID);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting Media: {ex.Message}");
                throw;
            }
        }
    }
}
