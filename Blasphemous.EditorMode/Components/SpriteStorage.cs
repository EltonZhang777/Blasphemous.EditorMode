using Blasphemous.ModdingAPI.Files;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Blasphemous.EditorMode.Components;
internal class SpriteStorage
{
    internal Sprite Cursor { get; private set; }

    internal SpriteStorage(FileHandler fileHandler)
    {
        fileHandler.LoadDataAsSprite("cursor.png", out Sprite cursor);
        Cursor = cursor;
    }
}
