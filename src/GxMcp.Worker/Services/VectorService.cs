using System;
using System.Collections.Generic;
using System.Linq;

namespace GxMcp.Worker.Services
{
    public class VectorService
    {
        // Simple TF-IDF inspired local embedding for GeneXus objects
        // This is a zero-dependency semantic bridge.
        
        public float[] ComputeEmbedding(string text)
        {
            if (string.IsNullOrEmpty(text)) return new float[128];
            
            // We use a fixed-size hashing vector for local comparison (SimHash-like)
            float[] vector = new float[128];
            var words = text.ToLower().Split(new[] { ' ', '.', ',', '(', ')', '[', ']', ':', ';' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var word in words)
            {
                int hash = word.GetHashCode();
                for (int i = 0; i < 128; i++)
                {
                    if (((hash >> (i % 32)) & 1) == 1)
                        vector[i] += 1.0f;
                    else
                        vector[i] -= 1.0f;
                }
            }

            // Normalize
            float magnitude = 0;
            for (int i = 0; i < 128; i++)
            {
                magnitude += vector[i] * vector[i];
            }
            magnitude = (float)Math.Sqrt(magnitude);

            if (magnitude > 0)
            {
                for (int i = 0; i < 128; i++) vector[i] /= magnitude;
            }
            
            return vector;
        }

        // Extremely fast dot product for normalized vectors using pointer arithmetic and loop unrolling.
        public unsafe float CosineSimilarity(float[] v1, float[] v2)
        {
            if (v1 == null || v2 == null || v1.Length != v2.Length) return 0f;

            float dotProduct = 0f;
            int length = v1.Length;

            fixed (float* p1 = v1)
            fixed (float* p2 = v2)
            {
                float* p1a = p1;
                float* p2a = p2;
                float* end = p1 + length;
                float* endUnrolled = p1 + (length - (length % 8));

                // Process in chunks of 8 to minimize loop overhead and allow superscalar execution
                while (p1a < endUnrolled)
                {
                    dotProduct += (p1a[0] * p2a[0]) + (p1a[1] * p2a[1]) +
                                  (p1a[2] * p2a[2]) + (p1a[3] * p2a[3]) +
                                  (p1a[4] * p2a[4]) + (p1a[5] * p2a[5]) +
                                  (p1a[6] * p2a[6]) + (p1a[7] * p2a[7]);

                    p1a += 8;
                    p2a += 8;
                }

                // Process the remaining elements
                while (p1a < end)
                {
                    dotProduct += (*p1a) * (*p2a);
                    p1a++;
                    p2a++;
                }
            }

            return dotProduct;
        }
    }
}
