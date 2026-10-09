#pragma once

#include <windows.h>
#include <stdio.h>

inline void PtpLog(const WCHAR* format, ...)
{
    WCHAR buffer[512];
    va_list args;
    va_start(args, format);
    _vsnwprintf_s(buffer, _countof(buffer), _TRUNCATE, format, args);
    va_end(args);
    OutputDebugStringW(buffer);
}
