cmake_minimum_required(VERSION 3.18)
project(muhenkit LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# ortak C++ çekirdek: muhenkit_native/core/mkcad
set(CORE ${CMAKE_CURRENT_SOURCE_DIR}/../../../../../core/mkcad)
# hesap çekirdeği: muhenkit_native/core/mkcalc
set(CALC ${CMAKE_CURRENT_SOURCE_DIR}/../../../../../core/mkcalc)

add_library(muhenkit SHARED
  jni_bridge.cpp
  ${CORE}/src/api.cpp
  ${CORE}/src/reader.cpp
  ${CORE}/src/scene.cpp
  ${CORE}/src/text.cpp
  ${CORE}/src/writer.cpp
  ${CORE}/src/edit.cpp
  ${CORE}/src/transform.cpp
  ${CORE}/src/offset.cpp
  ${CORE}/src/blocks.cpp
  ${CORE}/src/groups.cpp
  ${CALC}/src/calc.cpp
  ${CALC}/src/data.cpp
)
target_include_directories(muhenkit PRIVATE ${CORE}/include ${CORE}/src ${CALC}/include ${CALC}/src)
target_compile_options(muhenkit PRIVATE -O2 -fvisibility=hidden)
# 16 KB sayfa boyutu (Android 15+) uyumluluğu
target_link_options(muhenkit PRIVATE "-Wl,-z,max-page-size=16384")
target_link_libraries(muhenkit PRIVATE log)
