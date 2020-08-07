/**
 * Sample code for custom networking.
 */
#include <asio.hpp>

#include <cepton_sdk_api.hpp>

using asio::ip::udp;

class SocketListener {
 public:
  SocketListener() : m_socket(m_io_service, udp::v4()) {
    m_socket.set_option(asio::socket_base::reuse_address(true));
    m_socket.bind(udp::endpoint(udp::v4(), 8808));
  }

  void run() {
    listen();
    m_io_service.run_for(std::chrono::seconds(5));
  }

  void listen() {
    m_socket.async_receive_from(
        asio::buffer(m_buffer), m_end_point,
        [this](const asio::error_code& error, std::size_t buffer_size) {
          if (buffer_size == 0) return;
          if (error == asio::error::operation_aborted) return;
          const CeptonSensorHandle handle =
              m_end_point.address().to_v4().to_ulong();
          // For more accurate timestamps, a separate network receive thread
          // should be
          // used.
          const int64_t timestamp = cepton_sdk::util::get_timestamp_usec();
          CEPTON_CHECK_ERROR(cepton_sdk::mock_network_receive(
              handle, timestamp, m_buffer.data(), buffer_size));
          listen();
        });
  }

 private:
  asio::io_context m_io_service;
  udp::socket m_socket;
  udp::endpoint m_end_point;
  std::array<uint8_t, 4096> m_buffer;
};

int main() {
  // Initialize
  auto options = cepton_sdk::create_options();
  options.control_flags |= CEPTON_SDK_CONTROL_DISABLE_NETWORK;
  options.frame.mode = CEPTON_SDK_FRAME_COVER;
  CEPTON_CHECK_ERROR(cepton_sdk::api::initialize(options));

  // Listen for points
  cepton_sdk::api::SensorImageFrameCallback callback;
  CEPTON_CHECK_ERROR(callback.initialize());
  CEPTON_CHECK_ERROR(
      callback.listen([](cepton_sdk::SensorHandle handle, std::size_t n_points,
                         const cepton_sdk::SensorImagePoint* c_image_points) {
        std::printf("Received %i points from sensor %lli\n", (int)n_points,
                    (long long)handle);
      }));

  SocketListener listener;
  listener.run();

  // Deinitialize
  cepton_sdk::deinitialize().ignore();
}