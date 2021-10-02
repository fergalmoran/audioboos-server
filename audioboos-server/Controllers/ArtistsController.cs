using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AudioBoos.Data.Access;
using AudioBoos.Data.Models.DTO;
using AudioBoos.Data.Store;
using Microsoft.AspNetCore.Mvc;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AudioBoos.Server.Controllers {
    [ApiController]
    // [Authorize]
    [Route("[controller]")]
    public class ArtistsController : ControllerBase {
        private readonly IRepository<Artist> _artistRepository;

        public ArtistsController(IRepository<Artist> artistRepository) {
            _artistRepository = artistRepository;
        }

        [HttpGet]
        public async Task<ActionResult<List<ArtistDTO>>> Get() {
            var artists = await _artistRepository
                .GetAll()
                .OrderBy(r => r.Name)
                .ToListAsync();
            var results = artists.Adapt<List<ArtistDTO>>();
            return Ok(results);
        }

        [HttpGet("{artistName}")]
        public async Task<ActionResult<ArtistDTO>> Get(string artistName) {
            var artist = await _artistRepository
                .GetAll()
                .Include(a => a.Albums)
                .ThenInclude(a => a.Tracks)
                .FirstOrDefaultAsync(r => r.Name.Equals(artistName));
            return artist != null ? Ok(artist.Adapt<ArtistDTO>()) : NotFound();
        }
    }
}
