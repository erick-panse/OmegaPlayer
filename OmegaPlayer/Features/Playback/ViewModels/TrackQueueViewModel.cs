﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using OmegaPlayer.Core.ViewModels;
using OmegaPlayer.Features.Library.Models;
using OmegaPlayer.Features.Library.Services;
using OmegaPlayer.Features.Playback.Models;
using OmegaPlayer.Features.Playback.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace OmegaPlayer.Features.Playback.ViewModels
{
    public class TrackQueueUpdateMessage
    {
        public TrackDisplayModel CurrentTrack { get; }
        public ObservableCollection<TrackDisplayModel> Queue { get; }
        public int CurrentTrackIndex { get; }

        public TrackQueueUpdateMessage(TrackDisplayModel currentTrack, ObservableCollection<TrackDisplayModel> queue, int currentTrackIndex)
        {
            CurrentTrack = currentTrack;
            Queue = queue;
            CurrentTrackIndex = currentTrackIndex;
        }
    }

    public partial class TrackQueueViewModel : ViewModelBase
    {
        private readonly QueueService _queueService;
        private readonly TrackDisplayService _trackDisplayService;
        private readonly IMessenger _messenger;

        [ObservableProperty]
        private ObservableCollection<TrackDisplayModel> _nowPlayingQueue = new ObservableCollection<TrackDisplayModel>();

        [ObservableProperty]
        private TrackDisplayModel _currentTrack;

        [ObservableProperty]
        private int _currentQueueId;

        [ObservableProperty]
        private TimeSpan _remainingDuration;

        [ObservableProperty]
        private TimeSpan _totalDuration;

        private int _currentTrackIndex;

        public TrackQueueViewModel(QueueService queueService, TrackDisplayService trackDisplayService, IMessenger messenger)
        {
            _queueService = queueService;
            _trackDisplayService = trackDisplayService;
            _messenger = messenger;
            LoadLastPlayedQueue();
        }


        public async Task LoadLastPlayedQueue()
        {
            try
            {
                var result = await _queueService.GetCurrentQueueByProfileId(GetCurrentProfileId());

                if (result == null || result.Tracks == null || result.Tracks.Count == 0)
                {
                    Console.WriteLine("No last played queue found for the profile.");
                    return;
                }

                CurrentQueueId = result.CurrentQueueByProfile.QueueID;
                var trackDisplays = await _trackDisplayService.GetTrackDisplaysFromQueue(result.Tracks, GetCurrentProfileId());

                NowPlayingQueue.Clear(); // Clear existing queue first
                foreach (var track in trackDisplays)
                {
                    NowPlayingQueue.Add(track); // Add each TrackDisplay to the ObservableCollection
                }

                _currentTrackIndex = result.CurrentQueueByProfile.CurrentTrackOrder;
                // Set the last played track as the current track
                SetCurrentTrack(_currentTrackIndex);

                await UpdateDurations();
            }
            catch (Exception ex)
            {
                // Log the error but don't throw it, so the app doesn't crash
                Console.WriteLine($"Error loading last played queue: {ex.Message}");
            }
        }

        private int GetCurrentProfileId()
        {
            // Return the current profile ID based on the logged-in user
            return 2; // This should be dynamically set, placeholder for now
        }

        private async void SetCurrentTrack(int trackIndex)
        {
            CurrentTrack = trackIndex >= 0 && trackIndex < NowPlayingQueue.Count
            ? NowPlayingQueue[trackIndex]
            : null;

            await SaveCurrentTrack();
            await UpdateDurations();
            //await SaveNowPlayingQueue(); - FIX later
        }

        // Method to play a specific track and add others before/after it to the queue
        public void PlayThisTrack(TrackDisplayModel track, ObservableCollection<TrackDisplayModel> allTracks)
        {
            var index = allTracks.IndexOf(track);
            if (index == -1) return;

            // Clear the queue and add tracks before and after the selected track
            NowPlayingQueue.Clear();

            // Add tracks before the selected track
            for (int i = 0; i < index; i++)
            {
                NowPlayingQueue.Add(allTracks[i]);
            }

            // Add the selected track and start playing it
            NowPlayingQueue.Add(track);
            _currentTrackIndex = NowPlayingQueue.Count - 1; // Mark it as currently playing

            // Add tracks after the selected track
            for (int i = index + 1; i < allTracks.Count; i++)
            {
                NowPlayingQueue.Add(allTracks[i]);
            }

            SetCurrentTrack(_currentTrackIndex);
            _messenger.Send(new TrackQueueUpdateMessage(CurrentTrack, NowPlayingQueue, _currentTrackIndex));
        }


        
        public void AddToPlayNext()
        {
            // Add a list of tracks to play next
        }
        public void AddTrackToQueue()
        {
            // Add a list of tracks at the end of queue
        }



        public TrackDisplayModel GetNextTrack()
        {
            return NowPlayingQueue.Count - 1 >= _currentTrackIndex + 1 ? NowPlayingQueue[_currentTrackIndex + 1] : null;
        }

        public TrackDisplayModel GetPreviousTrack()
        {
            return _currentTrackIndex - 1 >= 0 ? NowPlayingQueue[_currentTrackIndex - 1] : null;
        }

        public void UpdateQueueAndTrack(ObservableCollection<TrackDisplayModel> newQueue, int newIndex)
        {
            NowPlayingQueue = newQueue;
            CurrentTrack = NowPlayingQueue[newIndex];
            _currentTrackIndex = newIndex;

        }

        // Method to save only the current track
        public async Task SaveCurrentTrack()
        {
            if (CurrentTrack != null)
            {
                await _queueService.SaveCurrentTrackAsync(CurrentQueueId, _currentTrackIndex, GetCurrentProfileId());
            }
        }

        // Method to save only the NowPlayingQueue (excluding the CurrentTrack)
        public async Task SaveNowPlayingQueue()
        {
            if (NowPlayingQueue.Any())
            {
                var queueTracks = NowPlayingQueue.Select((track, index) => new QueueTracks
                {
                    QueueID = CurrentQueueId,
                    TrackID = track.TrackID,
                    TrackOrder = index
                }).ToList();

                await _queueService.SaveNowPlayingQueueAsync(CurrentQueueId, queueTracks, GetCurrentProfileId());
            }
        }

        public async Task UpdateDurations()
        {
            TotalDuration = TimeSpan.FromTicks(NowPlayingQueue.Sum(t => t.Duration.Ticks));

            if (CurrentTrack != null)
            {
                var currentIndex = NowPlayingQueue.IndexOf(CurrentTrack);
                RemainingDuration = TimeSpan.FromTicks(
                    NowPlayingQueue.Skip(currentIndex).Sum(t => t.Duration.Ticks));
            }
        }


    }

}
