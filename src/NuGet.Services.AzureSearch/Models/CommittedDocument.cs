﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Search;
using Newtonsoft.Json;

namespace NuGet.Services.AzureSearch
{
    public abstract class CommittedDocument : KeyedDocument, ICommittedDocument
    {
        private readonly CurrentTimestamp _lastUpdatedDocument = new CurrentTimestamp();

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public DateTimeOffset? LastUpdatedDocument
        {
            get => _lastUpdatedDocument.Value;
            set => _lastUpdatedDocument.Value = value;
        }

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public string LastDocumentType { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public bool? LastUpdatedFromCatalog { get; set; }

        [IsSortable]
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public DateTimeOffset? LastCommitTimestamp { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public string LastCommitId { get; set; }

        public void SetLastUpdatedDocumentOnNextRead()
        {
            _lastUpdatedDocument.SetOnNextRead();
        }
    }
}