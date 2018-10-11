// Copyright 2018 Canaan Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Canaan.Kendryte.Flash.Shell.Services
{
    public class FlashFile
    {
        public uint Address { get; }

        private ZipArchiveEntry _bin;
        public Stream Bin => _bin.Open();

        public long Length => _bin.Length;

        public bool SHA256Prefix { get; }

        public FlashFile(uint address, ZipArchiveEntry bin, bool sha256Prefix)
        {
            Address = address;
            _bin = bin;
            SHA256Prefix = sha256Prefix;
        }
    }

    public class FlashPackage : IDisposable
    {
        private readonly ZipArchive _pkgArchive;
        private FlashListRoot _flashList;

        public IReadOnlyList<FlashFile> Files { get; private set; }

        public FlashPackage(Stream stream)
        {
            _pkgArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        }

        public async Task LoadAsync()
        {
            _flashList = await LoadFlashListAsync();

            Files = (from f in _flashList.Files
                     select new FlashFile(f.Address, _pkgArchive.GetEntry(f.Bin), f.SHA256Prefix)).ToList();
        }

        private async Task<FlashListRoot> LoadFlashListAsync()
        {
            using (var sr = new StreamReader(_pkgArchive.GetEntry("flash-list.json").Open()))
            {
                return JsonConvert.DeserializeObject<FlashListRoot>(await sr.ReadToEndAsync());
            }
        }

        private class FlashListRoot
        {
            public FlashListFile[] Files { get; set; }
        }

        private class FlashListFile
        {
            public uint Address { get; set; }
            public string Bin { get; set; }
            public bool SHA256Prefix { get; set; }
        }

        #region IDisposable Support
        private bool _disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _pkgArchive.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
