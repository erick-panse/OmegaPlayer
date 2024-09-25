﻿using Npgsql;
using System.Collections.Generic;
using System;
using System.Linq;
using OmegaPlayer.Models;
using System.Threading.Tasks;

namespace OmegaPlayer.Repositories
{
    public class TrackDisplayRepository
    {

        public async Task<List<TrackDisplayModel>> GetAllTracksWithMetadata(int profileId)
        {
            List<TrackDisplayModel> tracks = new List<TrackDisplayModel>();

            using (var db = new DbConnection())
            {
                string query = @"
                SELECT 
                    t.trackID, 
                    t.title, 
                    a.title AS albumTitle, 
                    t.duration, 
                    t.filePath, 
                    g.genreName AS genre, 
                    m.coverPath, 
                    t.releaseDate, 
                    t.playCount,
                    CASE WHEN l.trackID IS NOT NULL THEN true ELSE false END AS isLiked -- Check if track is liked
                FROM Tracks t
                LEFT JOIN Albums a ON t.albumID = a.albumID
                LEFT JOIN Genre g ON t.genreID = g.genreID
                LEFT JOIN Media m ON t.coverID = m.mediaID
                LEFT JOIN Likes l ON l.trackID = t.trackID AND l.profileID = @profileId -- Check Likes table for current user
                GROUP BY t.trackID, a.title, g.genreName, m.coverPath, l.trackID";

                using (var cmd = new NpgsqlCommand(query, db.dbConn))
                {
                    cmd.Parameters.AddWithValue("@profileId", profileId); // Pass the profileId as a parameter

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var track = new TrackDisplayModel
                            {
                                TrackID = reader.GetInt32(0),
                                Title = reader.GetString(1),
                                AlbumTitle = reader.IsDBNull(2) ? null : reader.GetString(2),
                                Duration = reader.GetTimeSpan(3),
                                FilePath = reader.GetString(4),
                                Genre = reader.IsDBNull(5) ? null : reader.GetString(5),
                                CoverPath = reader.IsDBNull(6) ? null : reader.GetString(6),
                                ReleaseDate = reader.IsDBNull(7) ? DateTime.MinValue : reader.GetDateTime(7),
                                PlayCount = reader.GetInt32(8),
                                IsLiked = reader.GetBoolean(9), // This will be true or false based on Likes table
                                Artists = new List<Artists>() // Initialize the Artists list
                            };

                            tracks.Add(track);
                        }
                    }
                }

                // Now get the artists for each track
                string artistQuery = @"
                SELECT 
                    ta.trackID, 
                    ar.artistID, 
                    ar.artistName 
                FROM TrackArtist ta
                INNER JOIN Artists ar ON ta.artistID = ar.artistID";

                using (var cmd = new NpgsqlCommand(artistQuery, db.dbConn))
                {
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int trackId = reader.GetInt32(0);
                            var artist = new Artists
                            {
                                ArtistID = reader.GetInt32(1),
                                ArtistName = reader.GetString(2)
                            };

                            // Find the corresponding track and add the artist to its list
                            var track = tracks.FirstOrDefault(t => t.TrackID == trackId);
                            track?.Artists.Add(artist);
                        }
                    }
                }
            }

            return tracks;
        }

        public async Task<List<TrackDisplayModel>> GetTracksWithMetadataAsync(int profileId, int pageNumber, int pageSize)
        {
            var tracks = new List<TrackDisplayModel>();

            string query = @"
            SELECT 
                t.trackID, 
                t.title, 
                a.title AS albumTitle, 
                t.duration, 
                t.filePath, 
                g.genreName AS genre, 
                m.coverPath, 
                t.releaseDate, 
                t.playCount,
                CASE WHEN l.trackID IS NOT NULL THEN true ELSE false END AS isLiked -- Check if track is liked
            FROM Tracks t
            LEFT JOIN Albums a ON t.albumID = a.albumID
            LEFT JOIN Genre g ON t.genreID = g.genreID
            LEFT JOIN Media m ON t.coverID = m.mediaID
            LEFT JOIN Likes l ON l.trackID = t.trackID AND l.profileID = @profileId -- Check Likes table for current user
            GROUP BY t.trackID, a.title, g.genreName, m.coverPath, l.trackID
            LIMIT @pageSize OFFSET @offset";

            using (var db = new DbConnection())
            {
                using (var cmd = new NpgsqlCommand(query, db.dbConn))
                {
                    cmd.Parameters.AddWithValue("@profileId", profileId); // Pass the profileId as a parameter
                    cmd.Parameters.AddWithValue("@pageSize", pageSize);
                    cmd.Parameters.AddWithValue("@offset", (pageNumber - 1) * pageSize);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var track = new TrackDisplayModel
                            {
                                TrackID = reader.GetInt32(0),
                                Title = reader.GetString(1),
                                AlbumTitle = reader.IsDBNull(2) ? null : reader.GetString(2),
                                Duration = reader.GetTimeSpan(3),
                                FilePath = reader.GetString(4),
                                Genre = reader.IsDBNull(5) ? null : reader.GetString(5),
                                CoverPath = reader.IsDBNull(6) ? null : reader.GetString(6),
                                ReleaseDate = reader.IsDBNull(7) ? DateTime.MinValue : reader.GetDateTime(7),
                                PlayCount = reader.GetInt32(8),
                                IsLiked = reader.GetBoolean(9), // This will be true or false based on Likes table
                                Artists = new List<Artists>() // Initialize the Artists list
                            };

                            tracks.Add(track);
                        }
                    }
                }

                // Get artists for paginated tracks
                string artistQuery = @"
                SELECT 
                    ta.trackID, 
                    ar.artistID, 
                    ar.artistName 
                FROM TrackArtist ta
                INNER JOIN Artists ar ON ta.artistID = ar.artistID
                WHERE ta.trackID = ANY(@trackIds)";

                // Collect track IDs for current page as an array
                var trackIds = tracks.Select(t => t.TrackID).ToArray();

                using (var cmd = new NpgsqlCommand(artistQuery, db.dbConn))
                {
                    // Add the track IDs as an array parameter
                    cmd.Parameters.AddWithValue("@trackIds", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer, trackIds);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int trackId = reader.GetInt32(0);
                            var artist = new Artists
                            {
                                ArtistID = reader.GetInt32(1),
                                ArtistName = reader.GetString(2)
                            };

                            // Find the corresponding track and add the artist to its list
                            var track = tracks.FirstOrDefault(t => t.TrackID == trackId);
                            track?.Artists.Add(artist);
                        }
                    }
                }
            }

            return tracks;
        }
    }
}
