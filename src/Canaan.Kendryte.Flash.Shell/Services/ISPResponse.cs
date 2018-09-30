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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Canaan.Kendryte.Flash.Shell.Services
{
    public class ISPResponse
    {
        public enum Operation
        {
            ISP_ECHO = 0xC1,
            ISP_NOP = 0xC2,
            ISP_MEMORY_WRITE = 0xC3,
            ISP_MEMORY_READ = 0xC4,
            ISP_MEMORY_BOOT = 0xC5,
            ISP_DEBUG_INFO = 0xD1
        }

        public enum ErrorCode
        {
            ISP_RET_DEFAULT = 0x00,
            ISP_RET_OK = 0xE0,
            ISP_RET_BAD_DATA_LEN = 0xE1,
            ISP_RET_BAD_DATA_CHECKSUM = 0xE2,
            ISP_RET_INVALID_COMMAND = 0xE3
        }

        public static (Operation op, ErrorCode errorCode) Parse(ReadOnlySpan<byte> rawData)
        {
            return ((Operation)rawData[0], (ErrorCode)rawData[1]);
        }
    }
}
