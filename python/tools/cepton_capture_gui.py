#!/usr/bin/env python3

import glob
import os
import os.path
import subprocess
import sys
import time

import imageio
import serial

from cepton_util.capture import *
from cepton_util.gui import *


class Capture:
    def __init__(self):
        self.window = QMainWindow()
        self.window.closeEvent = self.on_close

        self.update_callbacks = []  # Called every 100Hz
        self._update_timer = QTimer()
        self._update_timer.timeout.connect(self.update)
        self._update_timer.start(100)

        self.settings_dir = os.getcwd()
        try:
            self.network_interface = find_network_interface()
        except:
            self.network_interface = None
        self.enable_network = self.network_interface is not None
        self.camera_devices = []
        self.ros_topics = []
        self.serial_ports = []

        self.is_started = False
        self.capture = None
        self.dummy_capture = None
        self.camera_captures = []
        self.network_capture = None
        self.ros_capture = None
        self.serial_captures = []

        self.window.setWindowTitle("Cepton Capture")

        widget = QWidget()
        self.window.setCentralWidget(widget)
        self.create_toolbox(widget)

        self.window.show()

    def __del__(self):
        self.stop()

    def on_close(self, *args):
        self._update_timer.stop()

    def update(self):
        for f in self.update_callbacks:
            f()

    def create_toolbox(self, widget):
        layout = QVBoxLayout()
        widget.setLayout(layout)
        layout.setAlignment(Qt.AlignTop)

        self.create_toolbox_setup(layout)
        self.create_toolbox_monitor(layout)
        self.create_toolbox_info(layout)

        # Start
        start_stop = QPushButton("Start")
        layout.addWidget(start_stop)

        def on_start_stop():
            if self.is_started:
                self.stop()
            else:
                self.start()
        start_stop.clicked.connect(on_start_stop)

        def update():
            start_stop.setText("Stop" if self.is_started else "Start")
        self.update_callbacks.append(update)

    def create_toolbox_setup(self, parent_layout):
        widget = QGroupBox("Setup")
        parent_layout.addWidget(widget)
        layout = QFormLayout()
        widget.setLayout(layout)

        # Settings directory
        settings_dir_input = ButtonLineEdit(self.window.style().standardIcon(
            QStyle.SP_FileDialogStart))
        layout.addRow("Settings Directory", settings_dir_input)
        settings_dir_input.setText(self.settings_dir)

        def on_settings_dir():
            self.settings_dir = settings_dir_input.text()
        settings_dir_input.editingFinished.connect(on_settings_dir)

        def on_settings_dir():
            self.settings_dir = QFileDialog.getExistingDirectory(
                caption="Settings directory")
            if self.settings_dir is None:
                settings_dir_input.setText("")
            else:
                settings_dir_input.setText(self.settings_dir)
        settings_dir_input.button.clicked.connect(on_settings_dir)

        refresh_button = QPushButton("Refresh")
        layout.addRow(refresh_button)

        layout.addRow(create_toolbox_header("LiDAR"))

        # Enable network
        enable_network_input = QCheckBox("Enable")
        layout.addRow(enable_network_input)

        def on_enable_network():
            self.enable_network = enable_network_input.isChecked()
        enable_network_input.stateChanged.connect(on_enable_network)

        # Network interface
        network_interface_input = VariableLabel()
        layout.addRow("Interface", network_interface_input)

        layout.addRow(create_toolbox_header("Camera"))

        # Camera devices
        camera_devices_input = QListWidget()
        layout.addRow(create_expanding_label("Devices"), camera_devices_input)
        camera_devices_input.setFixedHeight(100)
        camera_devices_input.setSelectionMode(QAbstractItemView.MultiSelection)

        def on_camera_devices():
            self.camera_devices = \
                [x.text() for x in camera_devices_input.selectedItems()]
        camera_devices_input.selectionModel().selectionChanged.connect(on_camera_devices)

        layout.addRow(create_toolbox_header("ROS"))

        # ROS topics
        ros_topics_input = QListWidget()
        layout.addRow(create_expanding_label("Topics"), ros_topics_input)
        ros_topics_input.setFixedHeight(200)
        ros_topics_input.setSelectionMode(QAbstractItemView.MultiSelection)

        def on_ros_topics():
            self.ros_topics = \
                [x.text() for x in ros_topics_input.selectedItems()]
        ros_topics_input.selectionModel().selectionChanged.connect(on_ros_topics)

        layout.addRow(create_toolbox_header("Serial"))

        # Serial ports
        serial_ports_input = QListWidget()
        layout.addRow(create_expanding_label("Ports"), serial_ports_input)
        serial_ports_input.setFixedHeight(100)
        serial_ports_input.setSelectionMode(QAbstractItemView.MultiSelection)

        def on_serial_ports():
            self.serial_ports = \
                [x.text() for x in serial_ports_input.selectedItems()]
        serial_ports_input.selectionModel().selectionChanged.connect(on_serial_ports)

        def update():
            enable_network_input.setChecked(self.enable_network)
            if self.network_interface is None:
                enable_network_input.setEnabled(False)
                network_interface_input.setText("")
            else:
                enable_network_input.setEnabled(True)
                network_interface_input.setText(self.network_interface)
        self.update_callbacks.append(update)

        def on_refresh():
            camera_devices_input.clear()
            for device in get_all_camera_devices():
                item = QListWidgetItem(device)
                item.setSelected = device in self.camera_devices
                camera_devices_input.addItem(item)

            ros_topics_input.clear()
            for topic in get_all_ros_topics():
                item = QListWidgetItem(topic)
                item.setSelected = topic in self.ros_topics
                ros_topics_input.addItem(item)

            serial_ports_input.clear()
            for port in get_all_serial_ports():
                item = QListWidgetItem(port)
                item.setSelected = port in self.serial_ports
                serial_ports_input.addItem(item)

        on_refresh()
        refresh_button.pressed.connect(on_refresh)

    def create_toolbox_monitor(self, parent_layout):
        widget = QGroupBox("Monitor")
        parent_layout.addWidget(widget)
        layout = QFormLayout()
        widget.setLayout(layout)

        refresh_button = QPushButton("Refresh")
        layout.addRow(refresh_button)

        def on_refresh():
            while layout.count() > 1:
                layout.takeAt(1).widget().deleteLater()

            camera_readers = {}
            for device in get_all_camera_devices():
                try:
                    camera_readers[device] = imageio.get_reader(
                        "<{}>".format(os.path.basename(device)))
                except:
                    pass

            serial_readers = {}
            for port in get_all_serial_ports():
                try:
                    baudrate = int(subprocess.check_output(
                        "stty < {}".format(port), shell=True).split()[1])
                    serial_readers[port] = serial.Serial(
                        port, baudrate=baudrate, timeout=1)
                except:
                    continue

            time.sleep(1)

            layout.addRow(create_toolbox_header("Camera"))

            for device, reader in camera_readers.items():
                try:
                    image = reader.get_next_data()
                except:
                    continue
                finally:
                    reader.close()
                image = QImage(
                    image.tobytes(), image.shape[1], image.shape[0],
                    QImage.Format_RGB888)
                image = image.scaledToHeight(100)
                pixmap = QPixmap(image)

                canvas = QLabel()
                layout.addRow(create_expanding_label(device), canvas)
                canvas.setFixedHeight(100)
                canvas.setPixmap(pixmap)

            layout.addRow(create_toolbox_header("ROS"))

            for topic in get_all_ros_topics():
                try:
                    message = subprocess.check_output(
                        ["rostopic", "echo", "-n", "1"])[:20]
                except:
                    continue

                output = QLabel()
                layout.addRow(output)
                output.setText(line)

            layout.addRow(create_toolbox_header("Serial"))

            for port, reader in serial_readers.items():
                try:
                    line = reader.readline().decode("UTF-8")[:20]
                except:
                    continue
                finally:
                    reader.close()

                output = QLabel()
                layout.addRow(port, output)
                output.setText(line)

        refresh_button.pressed.connect(on_refresh)

        def update():
            refresh_button.setEnabled(not self.is_started)
        self.update_callbacks.append(update)

    def create_toolbox_info(self, parent_layout):
        widget = QGroupBox("Info")
        parent_layout.addWidget(widget)
        layout = QFormLayout()
        widget.setLayout(layout)

        # Path
        path_input = VariableLabel()
        layout.addRow("Path", path_input)

        # Length
        length_input = QLabel()
        layout.addRow("Length", length_input)

        def update():
            if self.capture is None:
                return
            path_input.setText(self.capture.path)
            length_input.setText("{:3d}s".format(
                int(round(self.dummy_capture.length))))
        self.update_callbacks.append(update)

    def start(self):
        if self.is_started:
            return

        self.is_started = True
        self.capture = OutputDataDirectory()
        if self.settings_dir is not None:
            self.capture.copy_settings(self.settings_dir)
        self.dummy_capture = CaptureBase()
        self.camera_captures = [
            CameraCapture(camera_device, self.capture.camera_path(i))
            for i, camera_device in enumerate(self.camera_devices)
        ]
        if self.network_interface is not None:
            self.network_capture = NetworkCapture(
                self.capture.network_path, interface=self.network_interface)
        if self.ros_topics:
            self.ros_capture = ROSCapture(
                self.ros_topics, self.capture.ros_path)
        self.serial_captures = [
            SerialCapture(serial_port, self.capture.serial_path(i))
            for i, serial_port in enumerate(self.serial_ports)
        ]

    def stop(self):
        if not self.is_started:
            return

        self.is_started = False
        if self.capture is not None:
            self.capture = None
        self.dummy_capture = None

        for camera_capture in self.camera_captures:
            camera_capture.close()
        self.camera_captures = []
        if self.network_capture is not None:
            self.network_capture.close()
            self.network_capture = None
        if self.ros_capture is not None:
            self.ros_capture.close()
            self.ros_capture = None
        for serial_capture in self.serial_captures:
            serial_capture.close()
        self.serial_captures = []


def main():
    app = QApplication(sys.argv)

    capture = Capture()
    code = app.exec_()
    sys.exit(code)


if __name__ == "__main__":
    main()
