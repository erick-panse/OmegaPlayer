﻿using OmegaPlayer.Features.Library.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OmegaPlayer.Features.Library.Services
{
    public class DirectoryScannerService
    {
        private readonly TracksService _trackService;
        private readonly TrackMetadataService _trackDataService;
        private readonly DirectoriesService _directoryService;
        private readonly BlacklistedDirectoryService _blacklistService;

        public DirectoryScannerService(
            TracksService trackService,
            DirectoriesService directoryService,
            BlacklistedDirectoryService blacklistService,
            TrackMetadataService trackDataService)
        {
            _trackService = trackService;
            _directoryService = directoryService;
            _blacklistService = blacklistService;
            _trackDataService = trackDataService;
        }


        public async Task ScanDirectoriesAsync(List<Directories> directories, int profileId)
        {

            var blacklistedPaths = await _blacklistService.GetBlacklistedDirectories(profileId);

            // Extract path from each Blacklist object into a List<string>
            var blacklistedPathsList = blacklistedPaths.Select(b => b.Path).ToList();

            foreach (var directory in directories)
            {
                // Check if the directory path is not in the blacklisted paths
                if (!blacklistedPathsList.Contains(directory.DirPath))
                {
                    await ScanDirectoryAsync(directory.DirPath);
                }
            }
        }

        private async Task ScanDirectoryAsync(string path)
        {
            var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                                .Where(f => f.EndsWith(".mp3") || f.EndsWith(".wav") || f.EndsWith(".flac"));  // Add supported formats

            foreach (var file in files)
            {
                var track = await _trackService.GetTrackByPath(file);
                var fileInfo = new FileInfo(file);

                if (track == null)  // If the track doesn't exist, add it
                {
                    await _trackDataService.PopulateTrackMetadata(file);
                }
                else if (fileInfo.LastWriteTime > track.UpdatedAt)  // If the file has been modified, update it
                {
                    await _trackDataService.UpdateTrackMetada(file);// create this in _trackDataService
                }
            }
        }
    }
}
