// DXF metin kaçışları: \U+XXXX, %%c/%%d/%%p/%%u, %%nnn ve MTEXT biçim kodları.
#include <cctype>
#include <cstdlib>

#include "document.h"

namespace mk {

static void putUtf8(std::string& out, uint32_t cp) {
  if (cp < 0x80) {
    out.push_back(static_cast<char>(cp));
  } else if (cp < 0x800) {
    out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
    out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
  } else {
    out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
    out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
    out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
  }
}

static bool isHex(char c) { return std::isxdigit(static_cast<unsigned char>(c)) != 0; }

std::string cleanText(const std::string& s) {
  std::string out;
  out.reserve(s.size());
  for (size_t i = 0; i < s.size();) {
    // \U+XXXX
    if (s[i] == '\\' && i + 6 < s.size() + 1 && i + 2 < s.size() && (s[i + 1] == 'U' || s[i + 1] == 'u') && s[i + 2] == '+' &&
        i + 6 < s.size() + 1 && i + 7 <= s.size() && isHex(s[i + 3]) && isHex(s[i + 4]) && isHex(s[i + 5]) && isHex(s[i + 6])) {
      uint32_t cp = static_cast<uint32_t>(std::strtoul(s.substr(i + 3, 4).c_str(), nullptr, 16));
      putUtf8(out, cp);
      i += 7;
      continue;
    }
    if (s[i] == '%' && i + 2 < s.size() && s[i + 1] == '%') {
      char c = static_cast<char>(std::tolower(static_cast<unsigned char>(s[i + 2])));
      if (c == 'c') { putUtf8(out, 0x00D8); i += 3; continue; }
      if (c == 'd') { putUtf8(out, 0x00B0); i += 3; continue; }
      if (c == 'p') { putUtf8(out, 0x00B1); i += 3; continue; }
      if (c == 'u' || c == 'o' || c == 'k') { i += 3; continue; }
      if (c == '%') { out.push_back('%'); i += 3; continue; }
      if (i + 4 < s.size() && std::isdigit((unsigned char)s[i + 2]) && std::isdigit((unsigned char)s[i + 3]) &&
          std::isdigit((unsigned char)s[i + 4])) {
        putUtf8(out, static_cast<uint32_t>(std::atoi(s.substr(i + 2, 3).c_str())));
        i += 5;
        continue;
      }
    }
    out.push_back(s[i]);
    i++;
  }
  return out;
}

std::string cleanMText(const std::string& s) {
  std::string out;
  out.reserve(s.size());
  for (size_t i = 0; i < s.size();) {
    char c = s[i];
    if (c == '{' || c == '}') { i++; continue; }
    if (c != '\\' || i + 1 >= s.size()) { out.push_back(c); i++; continue; }
    char k = s[i + 1];
    switch (k) {
      case 'P': case 'p':
        out.push_back('\n'); i += 2; break;
      case '~':
        out.push_back(' '); i += 2; break;
      case '\\': case '{': case '}':
        out.push_back(k); i += 2; break;
      case 'L': case 'l': case 'O': case 'o': case 'K': case 'k':
        i += 2; break;
      case 'S': {  // yığın: \Sa^b;  → a/b
        size_t e = s.find(';', i);
        std::string body = s.substr(i + 2, (e == std::string::npos ? s.size() : e) - i - 2);
        for (char& ch : body)
          if (ch == '^' || ch == '#') ch = '/';
        out += body;
        i = e == std::string::npos ? s.size() : e + 1;
        break;
      }
      case 'U': case 'u':  // \U+XXXX: cleanText çözer
        out.push_back(c); i++; break;
      case 'f': case 'F': case 'H': case 'h': case 'C': case 'c': case 'W': case 'w': case 'Q': case 'q':
      case 'T': case 't': case 'A': case 'a': {
        size_t e = s.find(';', i);
        i = e == std::string::npos ? s.size() : e + 1;
        break;
      }
      default:
        out.push_back(c); i++;
    }
  }
  return cleanText(out);
}

}  // namespace mk
