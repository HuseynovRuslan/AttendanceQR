using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using AttendanceQR.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AttendanceQR.Infrastructure.Services;

public sealed class RekognitionFaceMatchService : IFaceMatchService
{
    private readonly IAmazonRekognition _reko;
    private readonly RekognitionOptions _options;
    private readonly ILogger<RekognitionFaceMatchService> _logger;

    public RekognitionFaceMatchService(IAmazonRekognition reko, IOptions<RekognitionOptions> options, ILogger<RekognitionFaceMatchService> logger)
    {
        _reko = reko;
        _options = options.Value;
        _logger = logger;
    }

    public bool Enabled => !string.IsNullOrWhiteSpace(_options.AccessKey) && !string.IsNullOrWhiteSpace(_options.SecretKey);

    public async Task<FaceMatchOutcome> CompareAsync(byte[] referenceBytes, byte[] checkInBytes, CancellationToken ct = default)
    {
        if (!Enabled)
            return new FaceMatchOutcome(0, 0, FaceMatchStatus.NotChecked);

        try
        {
            using var refStream = new MemoryStream(referenceBytes, writable: false);
            using var chkStream = new MemoryStream(checkInBytes, writable: false);
            var resp = await _reko.CompareFacesAsync(new CompareFacesRequest
            {
                SourceImage = new Image { Bytes = refStream },
                TargetImage = new Image { Bytes = chkStream },
                SimilarityThreshold = 0F, // return everything; we bucket by our own threshold
            }, ct);

            // Faces detected in the check-in (target) = matched + unmatched.
            var faceCount = resp.FaceMatches.Count + resp.UnmatchedFaces.Count;
            var best = resp.FaceMatches.Count > 0 ? resp.FaceMatches.Max(m => m.Similarity ?? 0f) : 0f;
            var score = (int)Math.Round(best);

            var status = faceCount == 0 ? FaceMatchStatus.NoFace
                : faceCount > 1 ? FaceMatchStatus.MultiFace
                : score >= _options.SimilarityThreshold ? FaceMatchStatus.Ok
                : FaceMatchStatus.Mismatch;

            return new FaceMatchOutcome(score, faceCount, status);
        }
        catch (InvalidParameterException)
        {
            // Rekognition throws this when it finds no face in the source (reference) or target photo.
            return new FaceMatchOutcome(0, 0, FaceMatchStatus.NoFace);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Face match: CompareFaces failed");
            return new FaceMatchOutcome(0, 0, FaceMatchStatus.Error);
        }
    }
}
