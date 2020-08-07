#!/usr/bin/env python3
"""
Sample script for the different methods of getting points.
"""

import numpy

import cepton_sdk
from common import *


def on_frame(serial_number, points):
    print("Received {} points from sensor {}".format(
        len(points), serial_number))


if __name__ == "__main__":
    # Initialize
    cepton_sdk.initialize(capture_path=get_sample_capture_path())
    sensors_dict = cepton_sdk.get_sensors()
    sensor = next(iter(sensors_dict.values()))

    callback_id = cepton_sdk.listen_frames(on_frame)
    cepton_sdk.wait(0.1)
    cepton_sdk.unlisten_frames(callback_id)

    # Get next frames for all sensors. Wait until data is available.
    listener = cepton_sdk.FramesListener()
    points_dict = listener.get_points()
    del listener

    # Get next frames for single sensor. Wait until data is available.
    listener = cepton_sdk.SensorFramesListener(sensor.serial_number)
    points_list = listener.get_points()
    del listener

    # Get large chunk of data
    listener = cepton_sdk.FramesListener()
    cepton_sdk.wait(10)
    points_dict = listener.get_points()
    del listener
    points = cepton_sdk.combine_points(points_dict[sensor.serial_number])
    print("Received {} seconds of data from sensor {}".format(
        numpy.ptp(points.timestamps), sensor.serial_number))
