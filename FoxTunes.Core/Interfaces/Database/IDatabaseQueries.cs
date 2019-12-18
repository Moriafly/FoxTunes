﻿using FoxDb.Interfaces;
using System.Collections.Generic;

namespace FoxTunes.Interfaces
{
    public interface IDatabaseQueries : IBaseComponent
    {
        IDatabaseQuery AddLibraryHierarchyNode { get; }

        IDatabaseQuery AddLibraryHierarchyNodeToPlaylist { get; }

        IDatabaseQuery AddLibraryHierarchyNodeToPlaylistWithFilter { get; }

        IDatabaseQuery AddLibraryMetaDataItem { get; }

        IDatabaseQuery AddPlaylistMetaDataItem { get; }

        IDatabaseQuery BuildLibraryHierarchies(IEnumerable<string> metaDataNames);

        IDatabaseQuery ClearLibraryMetaDataItems { get; }

        IDatabaseQuery ClearPlaylistMetaDataItems { get; }

        IDatabaseQuery GetIsFavorite { get; }

        IDatabaseQuery GetLibraryMetaData { get; }

        IDatabaseQuery GetLibraryHierarchyMetaData { get; }

        IDatabaseQuery GetLibraryHierarchyNodes { get; }

        IDatabaseQuery GetLibraryHierarchyNodesWithFilter { get; }

        IDatabaseQuery GetLibraryItems { get; }

        IDatabaseQuery GetLibraryItemsWithFilter { get; }

        IDatabaseQuery GetOrAddMetaDataItem { get; }

        IDatabaseQuery MovePlaylistItem { get; }

        IDatabaseQuery RemoveLibraryHierarchyItems { get; }

        IDatabaseQuery RemoveLibraryItems { get; }

        IDatabaseQuery RemovePlaylistItems { get; }

        IDatabaseQuery SetIsFavorite { get; }

        IDatabaseQuery SequencePlaylistItems(IEnumerable<string> metaDataNames);

        IDatabaseQuery UpdateLibraryHierarchyNode { get; }

        IDatabaseQuery UpdateLibraryVariousArtists { get; }

        IDatabaseQuery UpdatePlaylistVariousArtists { get; }
    }
}
