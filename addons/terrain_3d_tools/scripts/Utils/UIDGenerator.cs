using System;
using System.Text;
using Godot;

namespace Terrain3DTools.Utils
{
    public static class IdGenerator
    {
        /// <summary>
        /// Generates a new short, URL-safe Base64-encoded UID.
        /// </summary>
        public static string GenerateShortUid()
        {
            Guid guid = Guid.NewGuid();
            string base64 = Convert.ToBase64String(guid.ToByteArray());

            // Replace URL-unsafe characters
            base64 = base64.Replace("=", "").Replace("+", "-").Replace("/", "_");

            return base64;
        }
    }
}