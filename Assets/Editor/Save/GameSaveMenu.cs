using System.IO;
using AnimalGame.Save;
using UnityEditor;
using UnityEngine;

namespace AnimalGame.Editor
{
    public static class GameSaveMenu
    {
        [MenuItem("Animal Game/Saves/Clear Save")]
        public static void ClearSave()
        {
            File.Delete(SaveSystem.SavePath);
            Debug.Log($"Deleted game save: {SaveSystem.SavePath}");
        }
    }
}
