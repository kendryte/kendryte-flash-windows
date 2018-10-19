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

namespace Canaan.Kendryte.Flash
{
    public class FlashModeResponse
    {
        public enum Operation
        {
            ISP_DEBUG_INFO = 0xD1,
            ISP_NOP = 0xD2,
            ISP_FLASH_ERASE = 0xD3,
            ISP_FLASH_WRITE = 0xD4,
            ISP_REBOOT = 0xD5,
            ISP_UARTHS_BAUDRATE_SET = 0xD6,
            FLASHMODE_FLASH_INIT = 0xD7
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
