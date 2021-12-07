﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AudioBoos.Data.Access;
using AudioBoos.Data.Models.Settings;
using AudioBoos.Data;
using AudioBoos.Data.Interfaces;
using AudioBoos.Data.Store;
using AudioBoos.Server.Services.AudioLookup;
using AudioBoos.Server.Services.Exceptions.AudioLookup;
using AudioBoos.Server.Services.Hubs;
using AudioBoos.Server.Services.Tags;
using Mapster;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudioBoos.Server.Services.Jobs.Scanners;

internal abstract class LibraryScanner : ILibraryScanner {
    protected readonly ILogger _logger;
    private readonly AudioBoosContext _context;
    protected readonly IAudioRepository<AudioFile> _audioFileRepository;
    protected readonly IAudioRepository<Artist> _artistRepository;
    protected readonly IAudioRepository<Album> _albumRepository;
    protected readonly IAudioRepository<Track> _trackRepository;
    protected readonly IHubContext<JobHub> _messageClient;
    protected readonly IUnitOfWork _unitOfWork;
    protected readonly IAudioLookupService _lookupService;
    private readonly IOptions<SystemSettings> _systemSettings;

    protected readonly SemaphoreSlim __scanLock = new(1, 1);


    protected LibraryScanner(ILogger<LibraryScanner> logger,
        AudioBoosContext context,
        IAudioRepository<AudioFile> audioFileRepository,
        IAudioRepository<Artist> artistRepository,
        IAudioRepository<Album> albumRepository,
        IAudioRepository<Track> trackRepository,
        IHubContext<JobHub> messageClient,
        IUnitOfWork unitOfWork,
        IAudioLookupService lookupService,
        IOptions<SystemSettings> systemSettings
    ) {
        _logger = logger;
        _context = context;
        _audioFileRepository = audioFileRepository;
        _artistRepository = artistRepository;
        _albumRepository = albumRepository;
        _trackRepository = trackRepository;
        _messageClient = messageClient;
        _unitOfWork = unitOfWork;
        _lookupService = lookupService;
        _systemSettings = systemSettings;
    }

    protected async Task<string> _libraryPath() => await _context
        .Settings
        .Where(r => r.Key.ToLower().Equals("librarypath"))
        .Select(s => s.Value)
        .FirstOrDefaultAsync();


    public abstract Task<(int, int, int)> ScanLibrary(bool deepScan, string childFolder,
        CancellationToken cancellationToken);

    public async Task UpdateUnscannedAlbums(CancellationToken cancellationToken) {
        _logger.LogInformation("Scanning unscanned albums");
        var unscannedAlbums = _albumRepository
                .GetAll()
                .AsNoTracking()
                .Include(a => a.Artist)
                .Where(a => a.TaggingStatus != TaggingStatus.ManualUpdate)
                .ToList()
                .Where(a => Album.IsIncomplete(a) || a.TaggingStatus.Equals(TaggingStatus.MP3TagsOnly))
            // .Where(a => a.Artist.Name.Equals("Bauhaus"))
            ;

        foreach (var album in unscannedAlbums) {
            try {
                _logger.LogInformation("Scanning {Album}", album.Name);
                var remoteAlbumInfo = await _lookupService.LookupAlbumInfo(
                    album.Artist.Name,
                    album.Name,
                    album.Id.ToString(),
                    cancellationToken);
                if (remoteAlbumInfo is null) {
                    continue;
                }

                var updated = remoteAlbumInfo.Adapt(album);
                updated.TaggingStatus = TaggingStatus.RemoteLookup;
                updated.LastScanDate = DateTime.Now;
                await _albumRepository.InsertOrUpdate(updated, cancellationToken);
                await _unitOfWork.Complete();
            } catch (AlbumNotFoundException) {
                _logger.LogError("Unable to find album info for {Artist} - {Album}", album.Artist.Name, album.Name);
            }
        }

        _logger.LogInformation("Finished scanning albums");
    }

    public async Task UpdateUnscannedArtists(CancellationToken cancellationToken) {
        var unscannedArtists = _artistRepository
            .GetAll()
            .AsNoTracking()
            .Where(a => a.TaggingStatus != TaggingStatus.ManualUpdate) //never auto scan manually updated artists
            .ToList()
            .Where(a => Artist.IsIncomplete(a) || a.TaggingStatus.Equals(TaggingStatus.MP3TagsOnly));

        foreach (var artist in unscannedArtists) {
            await UpdateArtist(artist.Name, cancellationToken);
        }
    }

    public async Task UpdateArtist(string artistName, CancellationToken cancellationToken) {
        _logger.LogDebug("Looking up info for {Artist}", artistName);
        var artist = await _artistRepository.GetByName(artistName, cancellationToken);
        try {
            var remoteArtistInfo = await _lookupService.LookupArtistInfo(
                artist.Name,
                cancellationToken);
            if (remoteArtistInfo is null) {
                return;
            }

            var updated = remoteArtistInfo.Adapt(artist);
            updated.TaggingStatus = TaggingStatus.RemoteLookup;

            await _artistRepository.InsertOrUpdate(updated, cancellationToken);
            await _unitOfWork.Complete();
        } catch (ArtistNotFoundException) {
            _logger.LogWarning("Artist {Artist} not found in {Scanner}", artist.Name, _lookupService.Name);
        } catch (Exception e) {
            _logger.LogError("Failure finding artist {Artist} in {Scanner}", artist.Name, _lookupService.Name);
            _logger.LogError("{Error}", e.Message);
        }

        _logger.LogInformation("Finished processing artists");
    }

    public async Task UpdateChecksums(CancellationToken cancellationToken) {
        var unscannedFiles = await _audioFileRepository
            .GetAll()
            .Where(a => string.IsNullOrEmpty(a.Checksum))
            .ToListAsync(cancellationToken);

        foreach (var audioFile in unscannedFiles) {
            _logger.LogDebug("Calculating checksum for {File}", audioFile.PhysicalPath);
            using var tagger = new TagLibTagService(audioFile.PhysicalPath);
            var checksum = await tagger.GetChecksum();
            audioFile.Checksum = checksum;
            await _audioFileRepository.InsertOrUpdate(audioFile, cancellationToken);
        }

        await _unitOfWork.Complete();
    }
}
