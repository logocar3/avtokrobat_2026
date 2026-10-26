// The one place where experiment-frame coordinates become Unity coordinates.
//
//   experiment frame (config, environments, experiments, CSVs): right-handed, +Z up, metres,
//                     x forward (towards the ramp), y left, z up
//   Unity:            left-handed, +Y up, metres (+X right, +Y up, +Z forward)
//
// The map swaps Y and Z:  Unity(x, y, z) = experiment(x, z, y).
// Swapping two axes flips handedness (right -> left), which is exactly what we need.
// A box size maps the same way (length, width, height) -> (x, y, z) scale = (length, height, width).
using UnityEngine;

namespace Avtokrobat
{
    public static class FrameConvert
    {
        // position (experiment x, y, z) -> Unity
        public static Vector3 Pos(float x, float y, float z)
        {
            return new Vector3(x, z, y);
        }

        // box size (experiment-local x, y, z) -> Unity localScale (Y and Z swap with the axes)
        public static Vector3 Size(float lx, float ly, float lz)
        {
            return new Vector3(lx, lz, ly);
        }
    }
}
