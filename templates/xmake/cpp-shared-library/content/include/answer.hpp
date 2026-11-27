#pragma once
#if defined(_WIN32)
#  if defined(PITSTOP_SHARED_EXPORTS)
#    define PITSTOP_SHARED_API __declspec(dllexport)
#  else
#    define PITSTOP_SHARED_API __declspec(dllimport)
#  endif
#else
#  define PITSTOP_SHARED_API __attribute__((visibility("default")))
#endif
PITSTOP_SHARED_API int answer();
