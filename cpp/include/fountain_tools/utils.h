#ifndef FOUNTAINUTILS_H
#define FOUNTAINUTILS_H

#include <string>

namespace Fountain {

std::string trim(const std::string& str);
std::string replaceAll(std::string str, const std::string& from, const std::string& to);
bool isWhitespaceOrEmpty(const std::string& str);

}

#endif // FOUNTAINUTILS_H