#include "answer.hpp"
#include <cstdio>

int main() {
    std::printf("%d\n", header_answer());
    return header_answer() == 42 ? 0 : 1;
}
