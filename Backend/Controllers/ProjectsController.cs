using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using ProjectManagementAPI.Models;
using ProjectManagementAPI.Services;
using StackExchange.Redis;
using System.Text.Json;

namespace ProjectManagementAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProjectsController : ControllerBase
    {
        private readonly MongoDbService _mongoService;
        private readonly IRedisService _redisService;




        public ProjectsController(MongoDbService mongoService, IRedisService redisService)
        {
            {
                _mongoService = mongoService;
                _redisService = redisService;


            }
        }

        // Helper property to get current user
        private string? UserEmail => HttpContext.Items["UserEmail"]?.ToString();

        // GET: api/projects
        [HttpGet]
        public async Task<IActionResult> GetProjects()
        {
            


           // if (UserEmail == null) return Unauthorized();  // commenting temporirily


            const string cacheKey = "projects:all";

            // Try Redis first
            var cachedProjects = await _redisService.GetAsync<List<Project>>(cacheKey);
            if (cachedProjects != null)
            {
                Console.WriteLine(" Returned from Redis cache.");
                return Ok(cachedProjects);
            }


            // If not in cache, fetch from MongoDB

            var projects = await _mongoService.Projects.Find(_ => true).ToListAsync();


            if (projects.Count == 0)
                return NotFound("No projects found.");

            // Store in cache for 10 minutes
            await _redisService.SetAsync(cacheKey, projects, TimeSpan.FromMinutes(10));
            Console.WriteLine(" Returned from MongoDB and cached in Redis.");

           
            return Ok(projects);



        }

        // GET: api/projects/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetProject(Guid id)
        {
            if (UserEmail == null) return Unauthorized();
            string cacheKey = $"project:{id}";

            // Try Redis first
            var cachedProject = await _redisService.GetAsync<Project>(cacheKey);
            if (cachedProject != null)
            {
                Console.WriteLine("Returned from Redis cache.");
                return Ok(cachedProject);
            }


            // Fetch from MongoDB
            var project = await _mongoService.Projects.Find(p => p.Id == id).FirstOrDefaultAsync();

            if (project == null) return NotFound();


            // Cache it for 10 minutes
            await _redisService.SetAsync(cacheKey, project, TimeSpan.FromMinutes(10));
            Console.WriteLine(" Returned from MongoDB and cached in Redis.");
           
            return Ok(project);


        }

        // POST: api/projects
        [HttpPost]
        public async Task<IActionResult> CreateProject(Project project)
        {
            //if (UserEmail == null) return Unauthorized();

            await _mongoService.Projects.InsertOneAsync(project);

            // Invalidate cache
            await _redisService.RemoveAsync("projects:all");

            Console.WriteLine(" Project created and Redis cache cleared.");
            return Ok(project);
        }

        // PUT: api/projects/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProject(Guid id, Project updatedProject)
        {
            if (UserEmail == null) return Unauthorized();

            var filter = Builders<Project>.Filter.Eq(p => p.Id, id);
            var update = Builders<Project>.Update
                .Set(p => p.Name, updatedProject.Name)
                .Set(p => p.Description, updatedProject.Description)
                .Set(p => p.Members, updatedProject.Members);

            var result = await _mongoService.Projects.UpdateOneAsync(filter, update);

            if (result.MatchedCount == 0) return NotFound();

            // Invalidate caches
            await _redisService.RemoveAsync("projects:all");
            await _redisService.RemoveAsync($"project:{id}");

            Console.WriteLine("Project updated and Redis cache cleared.");

            return NoContent();

        }

        // DELETE: api/projects/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProject(Guid id)
        {
           // if (UserEmail == null) return Unauthorized();

            var result = await _mongoService.Projects.DeleteOneAsync(p => p.Id == id);

            if (result.DeletedCount == 0) return NotFound();
            // Invalidate caches
            await _redisService.RemoveAsync("projects:all");
            await _redisService.RemoveAsync($"project:{id}");

            Console.WriteLine("Project deleted and Redis cache cleared.");

            return NoContent();
        }
    }
}
