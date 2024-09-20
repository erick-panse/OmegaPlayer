﻿using OmegaPlayer.Models;
using OmegaPlayer.Repositories;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OmegaPlayer.Services
{
    public class DirectoriesService
    {
        private readonly DirectoriesRepository _directoriesRepository;

        public DirectoriesService()
        {
            _directoriesRepository = new DirectoriesRepository();
        }

        public async Task<Directories> GetDirectoryById(int dirID)
        {
            try
            {
                return await _directoriesRepository.GetDirectoryById(dirID);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching Directory by ID: {ex.Message}");
                throw;
            }
        }

        public async Task<List<Directories>> GetAllDirectories()
        {
            try
            {
                return await _directoriesRepository.GetAllDirectories();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching all Directories: {ex.Message}");
                throw;
            }
        }

        public async Task<int> AddDirectory(Directories directory)
        {
            try
            {
                return await _directoriesRepository.AddDirectory(directory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding Directory: {ex.Message}");
                throw;
            }
        }

        public async Task DeleteDirectory(int dirID)
        {
            try
            {
                await _directoriesRepository.DeleteDirectory(dirID);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting Directory: {ex.Message}");
                throw;
            }
        }
    }
}
