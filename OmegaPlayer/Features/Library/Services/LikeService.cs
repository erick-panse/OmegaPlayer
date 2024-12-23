﻿using OmegaPlayer.Features.Library.Models;
using OmegaPlayer.Infrastructure.Data.Repositories.Library;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OmegaPlayer.Features.Library.Services
{
    public class LikeService
    {
        private readonly LikeRepository _likeRepository;

        public LikeService(LikeRepository likeRepository)
        {
            _likeRepository = likeRepository;
        }

        public async Task<Like> GetLikeById(int likeID)
        {
            try
            {
                return await _likeRepository.GetLikeById(likeID);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching Like by ID: {ex.Message}");
                throw;
            }
        }

        public async Task<List<Like>> GetAllLikes()
        {
            try
            {
                return await _likeRepository.GetAllLikes();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching all Likes: {ex.Message}");
                throw;
            }
        }

        public async Task<int> AddLike(Like like)
        {
            try
            {
                return await _likeRepository.AddLike(like);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding Like: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteLike(int likeID)
        {
            try
            {
                await _likeRepository.DeleteLike(likeID);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting Like: {ex.Message}");
                throw;
            }
        }
    }
}
