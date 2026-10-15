cmake_minimum_required(VERSION 3.18)
project(mkcad LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(mkcad SHARED
  src/api.cpp
  src/reader.cpp
  src/scene.cpp
  src/text.cpp
  src/writer.cpp
  src/edit.cpp
  src/transform.cpp
  src/offset.cpp
  src/blocks.cpp
  src/groups.cpp
)
target_include_directories(mkcad PUBLIC include PRIVATE src)
if(MSVC)
  target_compile_options(mkcad PRIVATE /O2 /utf-8)
else()
  target_compile_options(mkcad PRIVATE -O2 -fvisibility=hidden)
endif()

if(ANDROID)
  # 16 KB sayfa boyutu (Android 15+) uyumluluğu
  target_link_options(mkcad PRIVATE "-Wl,-z,max-page-size=16384")
endif()
