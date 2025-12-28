using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver.Core.Servers;
using ProjectManagementAPI.Models;
using ProjectManagementAPI.Services;
using System;

namespace ProjectManagementAPI.Hubs
{
    // This hub acts as the communication channel between backend and frontend
    public class NotificationHub : Hub
    {

        private readonly MongoDbService _mongoService;
        private readonly IRedisService _redisService;

        public NotificationHub(MongoDbService mongoService, IRedisService redisService)
        {
            _mongoService = mongoService;
            _redisService = redisService;
        }


        // Called by frontend immediately after connecting to SignalR
        // The frontend passes a list of project IDs that the current user is assigned to
        public async Task JoinProjectGroups(List<Guid> projectIds)
        {
            // Loop through all project IDs the user is part of
            foreach (var projectId in projectIds)
            {
                // Add the current user's SignalR connection to a group named after that project
                await Groups.AddToGroupAsync(Context.ConnectionId, $"project-{projectId}");

                // Log to the server console for debugging
                Console.WriteLine($"User {Context.ConnectionId} joined group project-{projectId}");
            }
        }


        // Send message to a project group
        public async Task SendMessageToProject(Guid projectId, string message, string senderName)
        {

            // make an object fopr storing in DB
            var chatMessage = new TeamChatMessage
            {
                ProjectId = projectId,
                SenderName = senderName,
                Message = message,
                Timestamp = DateTime.UtcNow
            };

            await _mongoService.TeamChatMessages.InsertOneAsync(chatMessage);

            // Update Redis cache add last 20
            string cacheKey = $"teamchat:{projectId}";

            var cached = await _redisService.GetAsync<List<TeamChatMessage>>(cacheKey) ?? new List<TeamChatMessage>();
            cached.Add(chatMessage);

            if (cached.Count > 20)
                cached = cached.Skip(cached.Count - 20).ToList(); // keep last 20

            // Reset TTL 
            await _redisService.SetAsync(cacheKey, cached, TimeSpan.FromMinutes(1));



            //send the message to the group based on projectId
            await Clients.Group($"project-{projectId}")
                         .SendAsync("ReceiveProjectMessage",  senderName, message, projectId );

            // Log to the server console for debugging
            Console.WriteLine($"User {senderName} sent a message to project group-{projectId}: {message}");


        }





    }
}
