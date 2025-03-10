﻿using Npgsql;
using OmegaPlayer.Features.Playback.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OmegaPlayer.Infrastructure.Data.Repositories.Playback
{
    public class QueueTracksRepository
    {
        public async Task<List<QueueTracks>> GetTracksByQueueId(int queueID)
        {
            var trackList = new List<QueueTracks>();

            try
            {
                using (var db = new DbConnection())
                {
                    string query = @"
                    SELECT * FROM QueueTracks 
                    WHERE QueueID = @queueID 
                    ORDER BY TrackOrder";

                    using (var cmd = new NpgsqlCommand(query, db.dbConn))
                    {
                        cmd.Parameters.AddWithValue("QueueID", queueID);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (reader.Read())
                            {
                                trackList.Add(new QueueTracks
                                {
                                    QueueID = reader.GetInt32(reader.GetOrdinal("QueueID")),
                                    TrackID = reader.GetInt32(reader.GetOrdinal("TrackID")),
                                    TrackOrder = reader.GetInt32(reader.GetOrdinal("TrackOrder")),
                                    OriginalOrder = reader.GetInt32(reader.GetOrdinal("OriginalOrder"))
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while fetching tracks from QueueTracks: {ex.Message}");
                throw;
            }

            return trackList;
        }

        public async Task AddTrackToQueue(List<QueueTracks> tracks)
        {
            try
            {
                using (var db = new DbConnection())
                {
                    foreach (var track in tracks)
                    {
                        string query = @"
                        INSERT INTO QueueTracks (QueueID, TrackID, TrackOrder, OriginalOrder) 
                        VALUES (@QueueID, @TrackID, @TrackOrder, @OriginalOrder)";

                        using (var cmd = new NpgsqlCommand(query, db.dbConn))
                        {
                            cmd.Parameters.AddWithValue("QueueID", track.QueueID);
                            cmd.Parameters.AddWithValue("TrackID", track.TrackID);
                            cmd.Parameters.AddWithValue("TrackOrder", track.TrackOrder);
                            cmd.Parameters.AddWithValue("OriginalOrder", track.OriginalOrder);

                            await cmd.ExecuteNonQueryAsync();
                        }
                    }

                    // Update LastModified in CurrentQueue
                    string updateQuery = @"
                    UPDATE CurrentQueue 
                    SET LastModified = CURRENT_TIMESTAMP 
                    WHERE QueueID = @QueueID";

                    using (var cmd = new NpgsqlCommand(updateQuery, db.dbConn))
                    {
                        cmd.Parameters.AddWithValue("QueueID", tracks.First().QueueID);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while adding tracks to QueueTracks: {ex.Message}");
                throw;
            }
        }

        public async Task RemoveTracksByQueueId(int queueID)
        {
            try
            {
                using (var db = new DbConnection())
                {
                    string query = "DELETE FROM QueueTracks WHERE QueueID = @QueueID";

                    using (var cmd = new NpgsqlCommand(query, db.dbConn))
                    {
                        cmd.Parameters.AddWithValue("QueueID", queueID);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while removing tracks from QueueTracks: {ex.Message}");
                throw;
            }
        }

        public async Task RemoveAllTracksByQueueId(int queueID)
        {
            try
            {
                using (var db = new DbConnection())
                {
                    string query = "DELETE * FROM QueueTracks WHERE QueueID = @QueueID";

                    using (var cmd = new NpgsqlCommand(query, db.dbConn))
                    {
                        cmd.Parameters.AddWithValue("QueueID", queueID);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while removing tracks from QueueTracks: {ex.Message}");
                throw;
            }
        }
    }
}
