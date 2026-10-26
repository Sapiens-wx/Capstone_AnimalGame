using System;
using Unity.Collections;
using UnityEngine;

namespace AnimalGame.MapTest
{
    /// <summary>
    /// Runtime-baked physical height field shared by rendering and gameplay.
    /// The raw-detail channel retains the resampled source precision, the detail
    /// channel can remove image quantization before step detection, and the
    /// independently smoothed surface channel is used for slopes and contours.
    /// </summary>
    public sealed class BakedHeightField : IDisposable, IMapHeightSource
    {
        private readonly HeightSamples rawDetailHeightsMeters;
        private readonly HeightSamples detailHeightsMeters;
        private readonly HeightSamples surfaceHeightsMeters;
        private readonly bool ownsTextures;
        // Retain asset owners while borrowing their native buffers; never dispose those views.
        private readonly HeightMapPrebakedData prebakedData;

        private readonly struct HeightSamples
        {
            private readonly float[] managed;
            private readonly NativeArray<float> borrowed;
            public int Length => managed != null ? managed.Length : borrowed.Length;
            public float this[int index] => managed != null ? managed[index] : borrowed[index];
            private HeightSamples(float[] values) { managed = values; borrowed = default; }
            private HeightSamples(NativeArray<float> values) { managed = null; borrowed = values; }
            public static implicit operator HeightSamples(float[] values) => new HeightSamples(values);
            public static implicit operator HeightSamples(NativeArray<float> values) => new HeightSamples(values);
#if UNITY_EDITOR
            public byte[] ToLittleEndianBytes()
            {
                var bytes = new byte[Length * sizeof(float)];
                Buffer.BlockCopy(managed ?? borrowed.ToArray(), 0, bytes, 0, bytes.Length);
                if (!BitConverter.IsLittleEndian)
                    for (int i = 0; i < bytes.Length; i += sizeof(float)) Array.Reverse(bytes, i, sizeof(float));
                return bytes;
            }
            public bool SameValues(HeightSamples other)
            {
                if (managed != null && ReferenceEquals(managed, other.managed)) return true;
                if (Length != other.Length) return false;
                for (int i = 0; i < Length; i++) if (this[i] != other[i]) return false;
                return true;
            }
#endif
        }

        public int Width { get; }
        public int Height { get; }
        public Vector2 MapSizeMeters { get; }
        public float MinimumHeightMeters { get; }
        public float MaximumHeightMeters { get; }
        public float SourceMinimum { get; }
        public float SourceMaximum { get; }
        public Texture2D SurfaceTexture { get; private set; }

        public float TexelSizeXMeters =>
            MapSizeMeters.x / Mathf.Max(1, Width - 1);

        public float TexelSizeYMeters =>
            MapSizeMeters.y / Mathf.Max(1, Height - 1);

        private BakedHeightField(
            int width,
            int height,
            Vector2 mapSizeMeters,
            float minimumHeightMeters,
            float maximumHeightMeters,
            float sourceMinimum,
            float sourceMaximum,
            HeightSamples rawDetailHeights,
            HeightSamples detailHeights,
            HeightSamples surfaceHeights,
            Texture2D surfaceTexture,
            bool ownsTextures = true,
            HeightMapPrebakedData prebakedData = null)
        {
            Width = width;
            Height = height;
            MapSizeMeters = mapSizeMeters;
            MinimumHeightMeters = minimumHeightMeters;
            MaximumHeightMeters = maximumHeightMeters;
            SourceMinimum = sourceMinimum;
            SourceMaximum = sourceMaximum;
            rawDetailHeightsMeters = rawDetailHeights;
            detailHeightsMeters = detailHeights;
            surfaceHeightsMeters = surfaceHeights;
            SurfaceTexture = surfaceTexture;
            this.ownsTextures = ownsTextures;
            this.prebakedData = prebakedData;
        }

        public static BakedHeightField Bake(
            Texture2D source,
            int requestedResolution,
            Vector2 mapSizeMeters,
            float minimumHeightMeters,
            float maximumHeightMeters,
            bool normalizeSourceRange,
            float smoothingSigmaMeters,
            float detailSmoothingSigmaMeters = 0f)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            int resolution = Mathf.Clamp(requestedResolution, 64, 4096);
            float[] sourcePixels = ReadSourceGrayscale(source);
            float sourceMinimum = 1f;
            float sourceMaximum = 0f;

            for (int index = 0; index < sourcePixels.Length; index++)
            {
                float gray = sourcePixels[index];
                sourceMinimum = Mathf.Min(sourceMinimum, gray);
                sourceMaximum = Mathf.Max(sourceMaximum, gray);
            }

            if (!normalizeSourceRange)
            {
                sourceMinimum = 0f;
                sourceMaximum = 1f;
            }

            float[] rawDetailHeights = new float[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                float v = y / (float)Mathf.Max(1, resolution - 1);
                for (int x = 0; x < resolution; x++)
                {
                    float u = x / (float)Mathf.Max(1, resolution - 1);
                    float sourceGray = SampleSource(
                        sourcePixels,
                        source.width,
                        source.height,
                        u,
                        v);
                    float normalized = Mathf.InverseLerp(
                        sourceMinimum,
                        sourceMaximum,
                        sourceGray);
                    rawDetailHeights[y * resolution + x] = Mathf.Lerp(
                        minimumHeightMeters,
                        maximumHeightMeters,
                        normalized);
                }
            }

            float texelSizeXMeters = mapSizeMeters.x / Mathf.Max(1, resolution - 1);
            float texelSizeYMeters = mapSizeMeters.y / Mathf.Max(1, resolution - 1);
            float detailSigmaPixelsX = detailSmoothingSigmaMeters /
                                       Mathf.Max(0.0001f, texelSizeXMeters);
            float detailSigmaPixelsY = detailSmoothingSigmaMeters /
                                       Mathf.Max(0.0001f, texelSizeYMeters);
            float[] detailHeights = detailSmoothingSigmaMeters > 0.0001f
                ? GaussianBlur(
                    rawDetailHeights,
                    resolution,
                    resolution,
                    detailSigmaPixelsX,
                    detailSigmaPixelsY)
                : rawDetailHeights;
            float sigmaPixelsX = smoothingSigmaMeters /
                                 Mathf.Max(0.0001f, texelSizeXMeters);
            float sigmaPixelsY = smoothingSigmaMeters /
                                 Mathf.Max(0.0001f, texelSizeYMeters);
            float[] surfaceHeights = GaussianBlur(
                rawDetailHeights,
                resolution,
                resolution,
                sigmaPixelsX,
                sigmaPixelsY);

            Texture2D surfaceTexture = CreateSurfaceTexture(
                surfaceHeights,
                resolution,
                resolution,
                minimumHeightMeters,
                maximumHeightMeters);
            return new BakedHeightField(
                resolution,
                resolution,
                mapSizeMeters,
                minimumHeightMeters,
                maximumHeightMeters,
                sourceMinimum,
                sourceMaximum,
                rawDetailHeights,
                detailHeights,
                surfaceHeights,
                surfaceTexture);
        }

        public bool TrySample(Vector2 position, out float height, int channel = 0)
        {
            height = 0;
            if (float.IsNaN(position.x) || float.IsNaN(position.y) || position.x < 0 || position.y < 0
                || position.x > MapSizeMeters.x || position.y > MapSizeMeters.y) return false;
            var uv = new Vector2(position.x / MapSizeMeters.x, position.y / MapSizeMeters.y);
            if (!IsPlayable(uv)) return false;
            height = channel == 1 ? SampleDetailHeight(uv) : channel == 2 ? SampleRawDetailHeight(uv) : SampleSurfaceHeight(uv);
            return true;
        }

        public float SampleSurfaceHeight(Vector2 uv)
        {
            return SampleBilinear(surfaceHeightsMeters, uv);
        }

        public float SampleDetailHeight(Vector2 uv)
        {
            return SampleBilinear(detailHeightsMeters, uv);
        }

        public float SampleRawDetailHeight(Vector2 uv)
        {
            return SampleBilinear(rawDetailHeightsMeters, uv);
        }

        /// <summary>All points within the rectangular tile bounds are playable.</summary>
        public bool IsPlayable(Vector2 uv) =>
            uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;

        /// <summary>
        /// Reads one sample from the same smoothed physical height field used by
        /// contour rendering and traversal. Intended for editor baking and visualization;
        /// runtime point and scan-profile queries use bilinear surface samples.
        /// </summary>
        public float GetSurfaceHeightSample(int x, int y)
        {
            int clampedX = Mathf.Clamp(x, 0, Width - 1);
            int clampedY = Mathf.Clamp(y, 0, Height - 1);
            return surfaceHeightsMeters[clampedY * Width + clampedX];
        }

        public static BakedHeightField FromPrebakedData(
            HeightMapPrebakedData data)
        {
            if (data == null || !data.HasHeightData) return null;
            // Bytes already contain meters. Borrow buffers without conversion or array allocation.
            var raw = data.RawDetailHeightData.GetData<float>();
            var detail = data.DetailHeightData.GetData<float>();
            var surface = data.SurfaceHeightData.GetData<float>();
            return new BakedHeightField(
                data.Resolution, data.Resolution, data.MapSizeMeters,
                data.MinimumHeightMeters, data.MaximumHeightMeters,
                data.SourceMinimum, data.SourceMaximum, raw, detail, surface,
                data.SurfaceTexture, false, data);
        }

        public void Dispose()
        {
            if (!ownsTextures)
                return;

            if (SurfaceTexture != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(SurfaceTexture);
                else
                    UnityEngine.Object.DestroyImmediate(SurfaceTexture);
                SurfaceTexture = null;
            }

        }

        private float SampleBilinear(HeightSamples values, Vector2 uv)
        {
            float pixelX = Mathf.Clamp01(uv.x) * (Width - 1);
            float pixelY = Mathf.Clamp01(uv.y) * (Height - 1);
            int x0 = Mathf.FloorToInt(pixelX);
            int y0 = Mathf.FloorToInt(pixelY);
            int x1 = Mathf.Min(x0 + 1, Width - 1);
            int y1 = Mathf.Min(y0 + 1, Height - 1);
            float tx = pixelX - x0;
            float ty = pixelY - y0;

            float bottom = Mathf.Lerp(
                values[y0 * Width + x0],
                values[y0 * Width + x1],
                tx);
            float top = Mathf.Lerp(
                values[y1 * Width + x0],
                values[y1 * Width + x1],
                tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        private static float GetGrayscale(Color32 color)
        {
            return (color.r * 0.299f + color.g * 0.587f + color.b * 0.114f) / 255f;
        }

        private static float[] ReadSourceGrayscale(Texture2D source)
        {
            int pixelCount = source.width * source.height;
            float[] samples = new float[pixelCount];

            // Unity imports an uncompressed 16-bit grayscale PNG as R16. Reading
            // its ushort payload directly avoids reducing the reconstructed DEM
            // back to the 256 levels exposed by GetPixels32().
            if (source.format == TextureFormat.R16)
            {
                var pixels = source.GetPixelData<ushort>(0);
                if (pixels.Length >= pixelCount)
                {
                    const float inverseUShort = 1f / ushort.MaxValue;
                    for (int index = 0; index < pixelCount; index++)
                        samples[index] = pixels[index] * inverseUShort;
                    return samples;
                }
            }

            if (source.format == TextureFormat.R8
                || source.format == TextureFormat.Alpha8)
            {
                var pixels = source.GetPixelData<byte>(0);
                if (pixels.Length >= pixelCount)
                {
                    const float inverseByte = 1f / byte.MaxValue;
                    for (int index = 0; index < pixelCount; index++)
                        samples[index] = pixels[index] * inverseByte;
                    return samples;
                }
            }

            Color32[] colors = source.GetPixels32();
            for (int index = 0; index < pixelCount; index++)
                samples[index] = GetGrayscale(colors[index]);
            return samples;
        }

#if UNITY_EDITOR
        public byte[] CreateRawDetailHeightData() => rawDetailHeightsMeters.ToLittleEndianBytes();

        // An absent detail payload encodes exact raw/detail deduplication.
        public byte[] CreateDetailHeightData() => detailHeightsMeters.SameValues(rawDetailHeightsMeters)
            ? null : detailHeightsMeters.ToLittleEndianBytes();

        public byte[] CreateSurfaceHeightData() => surfaceHeightsMeters.ToLittleEndianBytes();

        public Texture2D CreateSurfaceTextureCopy()
        {
            return CreateHeightTexture(surfaceHeightsMeters, Width, Height, MinimumHeightMeters,
                MaximumHeightMeters, "Pre-Baked Surface Height Field");
        }
#endif
        private static float SampleSource(
            float[] pixels,
            int width,
            int height,
            float u,
            float v)
        {
            float pixelX = Mathf.Clamp01(u) * (width - 1);
            float pixelY = Mathf.Clamp01(v) * (height - 1);
            int x0 = Mathf.FloorToInt(pixelX);
            int y0 = Mathf.FloorToInt(pixelY);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int y1 = Mathf.Min(y0 + 1, height - 1);
            float tx = pixelX - x0;
            float ty = pixelY - y0;

            float bottom = Mathf.Lerp(
                pixels[y0 * width + x0],
                pixels[y0 * width + x1],
                tx);
            float top = Mathf.Lerp(
                pixels[y1 * width + x0],
                pixels[y1 * width + x1],
                tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        private static float[] GaussianBlur(
            float[] source,
            int width,
            int height,
            float sigmaPixelsX,
            float sigmaPixelsY)
        {
            if (sigmaPixelsX <= 0.01f && sigmaPixelsY <= 0.01f)
                return (float[])source.Clone();

            float[] horizontalKernel = BuildGaussianKernel(sigmaPixelsX);
            float[] verticalKernel = BuildGaussianKernel(sigmaPixelsY);
            float[] horizontal = new float[source.Length];
            float[] output = new float[source.Length];
            int horizontalRadius = horizontalKernel.Length / 2;
            int verticalRadius = verticalKernel.Length / 2;

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    float value = 0f;
                    for (int offset = -horizontalRadius;
                         offset <= horizontalRadius;
                         offset++)
                    {
                        int sampleX = Mathf.Clamp(x + offset, 0, width - 1);
                        value += source[row + sampleX] *
                                 horizontalKernel[offset + horizontalRadius];
                    }

                    horizontal[row + x] = value;
                }
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float value = 0f;
                    for (int offset = -verticalRadius;
                         offset <= verticalRadius;
                         offset++)
                    {
                        int sampleY = Mathf.Clamp(y + offset, 0, height - 1);
                        value += horizontal[sampleY * width + x] *
                                 verticalKernel[offset + verticalRadius];
                    }

                    output[y * width + x] = value;
                }
            }

            return output;
        }





        private static float[] BuildGaussianKernel(float sigmaPixels)
        {
            if (sigmaPixels <= 0.01f)
                return new[] { 1f };

            int radius = Mathf.Max(1, Mathf.CeilToInt(sigmaPixels * 3f));
            float[] kernel = new float[radius * 2 + 1];
            float weightSum = 0f;
            float denominator = 2f * sigmaPixels * sigmaPixels;

            for (int offset = -radius; offset <= radius; offset++)
            {
                float weight = Mathf.Exp(-(offset * offset) / denominator);
                kernel[offset + radius] = weight;
                weightSum += weight;
            }

            for (int index = 0; index < kernel.Length; index++)
                kernel[index] /= weightSum;

            return kernel;
        }

        private static Texture2D CreateSurfaceTexture(
            HeightSamples heightsMeters,
            int width,
            int height,
            float minimumHeightMeters,
            float maximumHeightMeters)
        {
            return CreateHeightTexture(
                heightsMeters,
                width,
                height,
                minimumHeightMeters,
                maximumHeightMeters,
                "Baked Physical Height Field");
        }

        private static Texture2D CreateHeightTexture(
            HeightSamples heightsMeters,
            int width,
            int height,
            float minimumHeightMeters,
            float maximumHeightMeters,
            string textureName,
            bool makeNoLongerReadable = true)
        {
            var texture = new Texture2D(
                width,
                height,
                TextureFormat.RFloat,
                false,
                true)
            {
                name = textureName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            float[] normalizedHeights = new float[heightsMeters.Length];
            for (int index = 0; index < heightsMeters.Length; index++)
            {
                normalizedHeights[index] = Mathf.InverseLerp(
                    minimumHeightMeters,
                    maximumHeightMeters,
                    heightsMeters[index]);
            }

            texture.SetPixelData(normalizedHeights, 0);
            texture.Apply(false, makeNoLongerReadable);
            return texture;
        }


    }
}
